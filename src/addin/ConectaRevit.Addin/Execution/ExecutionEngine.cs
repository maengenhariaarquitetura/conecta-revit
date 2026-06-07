using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using ConectaRevit.Addin.Logging;
using ConectaRevit.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace ConectaRevit.Addin.Execution;

// Motor de execução na thread principal via ExternalEvent + TCS (ARCHITECTURE § 5.3).
//
// ┌─ DIAGRAMA DE FLUXO ──────────────────────────────────────────────────────┐
// │  WS thread      →  EnqueueAsync: encila job, chama ExternalEvent.Raise() │
// │  Revit Thread 1 →  Execute(): loga INICIADO, chama DispatchJob()         │
// │  Revit Thread 1 →  DispatchJob(): compila/roda script (síncrono), TCS   │
// │  WS thread      →  await TCS: recebe resultado, envia resposta WS        │
// └──────────────────────────────────────────────────────────────────────────┘
//
// THREADING CRÍTICO — MODIFICAÇÃO DE DOCUMENTO:
//   A API do Revit só permite modificar o documento na Thread 1, dentro do
//   contexto do ExternalEvent.Execute(). Task.Run() move a execução para o
//   ThreadPool — leitura passa, mas qualquer write lança InvalidOperationException
//   "Cannot modify the document". Por isso o script Roslyn DEVE ser executado
//   síncrono na Thread 1: .GetAwaiter().GetResult() direto em Execute(), sem
//   Task.Run. Não causa deadlock porque HandleExecuteCodeAsync (lado WS) aguarda
//   o TCS em thread de background — Thread 1 pode bloquear brevemente.
//
// INVARIANTE: todo caminho em Execute() DEVE resolver o TCS (TrySetResult ou
// TrySetException). Caso contrário o WS fica bloqueado para sempre.
//
// DESIGN CRÍTICO — ISOLAMENTO ROSLYN:
//   Execute() NÃO pode referenciar tipos Microsoft.CodeAnalysis.* em seu IL.
//   Quando o Revit despacha o ExternalEvent, o CLR precisa JIT-compilar Execute().
//   Se qualquer tipo referenciado em Execute() causar TypeLoadException/FileLoadException
//   (ex.: conflito de versão de DLL Roslyn), a JIT falha ANTES da primeira instrução
//   — sem log, sem catch, sem trace. O handler aparece como "nunca chamado".
//
//   Solução: todos os tipos Roslyn vivem em DispatchJob() e métodos abaixo.
//   Execute() chama DispatchJob() dentro de try/catch — se o JIT de DispatchJob
//   falhar, a TypeLoadException propaga para o catch de Execute() e é capturada.
internal sealed class ExecutionEngine : IExternalEventHandler
{
    private readonly string _revitVersion;
    private ExternalEvent?  _externalEvent;

    // Fila serial: um job por despacho de Execute() (ARCHITECTURE § 5.2, máx. 10).
    private readonly ConcurrentQueue<ExecuteJob> _queue = new();

    // Nome da última TransactionGroup confirmada — revert_last (Fase 3C).
    internal string? LastTransactionName { get; private set; }

    // ScriptOptions ISOLADO: não é referenciado em Execute() para não contaminar o IL.
    // Somente DispatchJob() e os métodos Roslyn abaixo tocam esse campo.
    private ScriptOptions? _scriptOptions;

    internal ExecutionEngine(string revitVersion) => _revitVersion = revitVersion;

    internal void SetExternalEvent(ExternalEvent ev) => _externalEvent = ev;

    // ── API pública ───────────────────────────────────────────────────────────

    internal Task<ExecuteCodeResult> EnqueueAsync(ExecuteCodeParams p, CancellationToken ct)
    {
        if (_queue.Count >= 10)
        {
            AddinLog.Warn("EnqueueAsync: fila cheia (>= 10). Rejeitando.");
            throw new InvalidOperationException("BUSY");
        }

        var job = new ExecuteJob(p, ct);
        _queue.Enqueue(job);

        // EngineHashCode DEVE ser idêntico ao logado em OnStartup e Execute().
        // EventHashCode DEVE ser idêntico ao logado em OnStartup.
        AddinLog.Info(
            $"EnqueueAsync: job {job.JobId} enfileirado. " +
            $"Thread={Environment.CurrentManagedThreadId} (esperado: ThreadPool). " +
            $"EngineHashCode={RuntimeHelpers.GetHashCode(this)}. " +
            $"EventHashCode={RuntimeHelpers.GetHashCode(_externalEvent!)}. " +
            $"Código={TruncateCode(p.Code, 80)}");

        var raiseResult = _externalEvent.Raise();

        AddinLog.Info(
            $"EnqueueAsync: job {job.JobId} — Raise()={raiseResult}. " +
            $"IsPending={_externalEvent.IsPending}.");

        if (raiseResult != ExternalEventRequest.Accepted)
        {
            AddinLog.Error(
                $"EnqueueAsync: Raise() não foi Accepted ({raiseResult}). TCS resolvido com erro.");
            job.Tcs.TrySetException(new InvalidOperationException(
                $"ExternalEvent.Raise() retornou {raiseResult}. " +
                "Tente desconectar e reconectar o servidor ConectaRevit."));
        }

        return job.Tcs.Task.WaitAsync(ct);
    }

    // ── IExternalEventHandler ─────────────────────────────────────────────────

    // ═══════════════════════════════════════════════════════════════════════════
    // REGRA DE OURO: Execute() NÃO pode conter tipos Microsoft.CodeAnalysis.*
    // em seu IL. Se o JIT de Execute() precisar resolver ScriptOptions/CSharpScript/
    // qualquer tipo Roslyn e não conseguir (conflito de DLL), a execução aborta
    // ANTES da primeira instrução — sem log, sem catch.
    //
    // Todos os tipos Roslyn estão em DispatchJob() e RunSafe()/BuildScriptOptions().
    // Execute() só usa: BCL + Revit API + nossos tipos (AddinLog, ExecuteJob, etc.)
    // ═══════════════════════════════════════════════════════════════════════════
    public void Execute(UIApplication app)
    {
        // ── PRIMEIRA LINHA ABSOLUTA ──────────────────────────────────────────
        // Usa apenas tipos BCL (Environment, RuntimeHelpers) e nosso AddinLog.
        // NENHUM tipo Roslyn referenciado aqui.
        // Se esta linha NÃO aparecer no log após GetName() ser chamado,
        // o JIT de Execute() falhou → verificar DispatchJob() isolado via smoke test.
        AddinLog.Info(
            $"Execute() INICIADO. " +
            $"Thread={Environment.CurrentManagedThreadId} (esperado: Thread 1). " +
            $"EngineHashCode={RuntimeHelpers.GetHashCode(this)}. " +
            $"FilaCount={_queue.Count}.");

        ExecuteJob? job = null;
        try
        {
            // Cacheia UIApplication para leitura da thread WS (volatile em Application).
            Application.UiApplication = app;

            if (!_queue.TryDequeue(out job))
            {
                AddinLog.Warn("Execute(): fila vazia (evento espúrio ou corrida). Ignorando.");
                return;
            }

            AddinLog.Info(
                $"Execute(): job {job.JobId} dequeued. " +
                $"Cancelado={job.Ct.IsCancellationRequested}. " +
                $"TemDocumento={app.ActiveUIDocument != null}.");

            if (job.Ct.IsCancellationRequested)
            {
                AddinLog.Info($"Execute(): job {job.JobId} cancelado antes de iniciar. TCS cancelado.");
                job.Tcs.TrySetCanceled(job.Ct);
                return;
            }

            // DispatchJob isola TODOS os tipos Roslyn.
            // Se o JIT de DispatchJob falhar (TypeLoadException por DLL Roslyn incompatível),
            // a exceção é capturada pelo catch(Exception) abaixo e o TCS é resolvido.
            DispatchJob(app, job);
        }
        catch (Exception ex)
        {
            // Catch-all universal — loga diagnóstico completo.
            // Abrange TypeLoadException, FileLoadException, TypeInitializationException,
            // ReflectionTypeLoadException, CompilationException, NoDocumentException, etc.
            AddinLog.Error($"Execute() EXCEÇÃO:\n{BuildExceptionDiag(ex)}");
            job?.Tcs.TrySetException(ex);
        }
        finally
        {
            AddinLog.Info(
                $"Execute() finally. " +
                $"job={job?.JobId ?? "null"}. " +
                $"TCS.IsCompleted={job?.Tcs.Task.IsCompleted}. " +
                $"FilaRestante={_queue.Count}.");

            if (!_queue.IsEmpty)
            {
                AddinLog.Info("Execute(): re-raise para próximo job.");
                _externalEvent!.Raise();
            }
        }
    }

    // GetName() é chamado pelo Revit para identificar o handler ANTES de Execute().
    // Deve ser public, nunca lançar e retornar string não-vazia.
    // O log aqui confirma que o Revit reconheceu a instância correta.
    public string GetName()
    {
        AddinLog.Info($"GetName() chamado. EngineHashCode={RuntimeHelpers.GetHashCode(this)}.");
        return "ConectaRevit.ExecutionEngine";
    }

    // ── DispatchJob: único ponto de entrada dos tipos Roslyn ─────────────────
    //
    // ISOLAMENTO: este método contém TODAS as referências a Microsoft.CodeAnalysis.*.
    // É chamado de Execute() dentro de try/catch. Se o JIT deste método falhar
    // por conflito de DLL Roslyn, o TypeLoadException/FileLoadException é capturado
    // em Execute() e o TCS é resolvido com RUNTIME_ERROR.
    //
    // NÃO mova tipos Roslyn de volta para Execute(). Não adicione tipos Roslyn
    // no catch/finally de Execute(). Mantenha esta fronteira.
    private void DispatchJob(UIApplication app, ExecuteJob job)
    {
        AddinLog.Info($"DispatchJob(): job {job.JobId} — JIT de DispatchJob OK (Roslyn DLLs acessíveis).");

        // ScriptOptions construído na primeira execução e reutilizado.
        // _scriptOptions é acessado SOMENTE aqui — mantendo o tipo ScriptOptions
        // fora do IL de Execute().
        if (_scriptOptions == null)
        {
            AddinLog.Info("DispatchJob(): construindo ScriptOptions (primeira execução).");
            _scriptOptions = BuildScriptOptions();
            AddinLog.Info("DispatchJob(): ScriptOptions construído com sucesso.");
        }

        AddinLog.Info($"DispatchJob(): job {job.JobId} — chamando RunScript.");
        var result = RunScript(app, job.Params);

        AddinLog.Info($"DispatchJob(): job {job.JobId} — RunScript OK. Resolvendo TCS com sucesso.");
        job.Tcs.TrySetResult(result);
    }

    // ── Smoke test Roslyn (chamado no OnStartup) ─────────────────────────────
    //
    // Verifica se os assemblies Roslyn carregam sem conflito de versão.
    // Chamado em Application.OnStartup ANTES do primeiro ExternalEvent para
    // identificar problemas antes do fluxo de execução de script.
    // O chamador deve envolver esta chamada em try/catch para capturar falhas de
    // JIT deste próprio método (que contém tipos Roslyn no IL).
    //
    // Estratégia de compatibilidade (documentada no .csproj):
    //   Roslyn 4.8.0 requer System.Collections.Immutable >= 8.0.0.
    //   .NET 8 shared framework provê 8.0.0 → compatível.
    //   Roslyn 4.9.x+ requer 9.0.0, incompatível com .NET 8 → FileLoadException.
    internal static void RunSmokeTest()
    {
        var roslynAsm = typeof(CSharpScript).Assembly;
        var sciAsm    = typeof(System.Collections.Immutable.ImmutableArray).Assembly;

        AddinLog.Info(
            $"RoslynSmokeTest: iniciando. " +
            $"Estratégia=Roslyn 4.8.0 (Opção B — versão compatível com .NET 8 shared framework). " +
            $"RoslynVersion={roslynAsm.GetName().Version}. " +
            $"ImmutableVersion={sciAsm.GetName().Version}. " +
            $"Avaliando '1 + 1'...");
        try
        {
            // Task.Run evita qualquer SynchronizationContext do OnStartup.
            var result = Task.Run(async () =>
                await CSharpScript.EvaluateAsync<int>("1 + 1").ConfigureAwait(false))
                .GetAwaiter().GetResult();

            AddinLog.Info(
                $"RoslynSmokeTest: SUCESSO. 1+1={result}. " +
                "Roslyn OK: CSharpScript, ScriptOptions e dependências carregaram sem FileLoadException.");
        }
        catch (Exception ex)
        {
            AddinLog.Error(
                $"RoslynSmokeTest: FALHOU. " +
                $"Diagnóstico:\n{BuildExceptionDiag(ex)}");
        }
    }

    // ── Despacho de modo ──────────────────────────────────────────────────────

    private ExecuteCodeResult RunScript(UIApplication app, ExecuteCodeParams p)
    {
        var effectiveMode = p.Mode?.ToLowerInvariant() switch
        {
            "safe"   => "safe",
            "direct" => "safe",   // TODO Fase 3D
            _        => Application.Settings?.Mode ?? "safe"
        };

        AddinLog.Info($"RunScript: modo efetivo = '{effectiveMode}'.");
        var logLines = new List<string>();

        return effectiveMode == "direct"
            ? RunDirect(app, p.Code, logLines)
            : RunSafe(app, p.Code, logLines);
    }

    // ── Modo Seguro ───────────────────────────────────────────────────────────

    private ExecuteCodeResult RunSafe(UIApplication app, string code, List<string> logLines)
    {
        var uiDoc = app.ActiveUIDocument ?? throw new NoDocumentException();
        var doc   = uiDoc.Document       ?? throw new NoDocumentException();

        AddinLog.Info(
            $"RunSafe: documento ativo = '{doc.Title}'. " +
            $"Thread={Environment.CurrentManagedThreadId} (esperado: Thread 1 — modificações exigem Thread 1).");

        var globals = new ScriptGlobals
        {
            Doc   = doc,
            UiDoc = uiDoc,
            UiApp = app,
            Log   = msg => { logLines.Add(msg); AddinLog.Info($"[ScriptLog] {msg}"); },
        };

        var txName = "ConectaRevit: " + SummarizeCode(code);

        // ── Fase 3B: rastreamento de elementos via DocumentChanged ────────────
        //
        // MECANISMO ESCOLHIDO: Application.DocumentChanged
        //   • Dispara na Thread 1 DURANTE tx.Commit() — síncrono, mesmo frame de execução.
        //   • Fornece exatamente GetAddedElementIds() / GetModifiedElementIds() /
        //     GetDeletedElementIds() para os elementos afetados NESTE commit.
        //   • Em rollback, o evento NÃO dispara → listas permanecem vazias. ✓
        //   • Alternativa (snapshot pré/pós) foi descartada: percorreria o FilteredElement-
        //     Collector duas vezes e perderia elementos deletados.
        //
        // GARANTIA DE DESINCRIÇÃO: a chamada "-= OnDocumentChanged" fica no finally
        // externo → executa em qualquer caminho (sucesso, erro de compilação, rollback).
        var createdIds  = new List<long>();
        var modifiedIds = new List<long>();
        var deletedIds  = new List<long>();

        void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
        {
            // Filtra por documento — múltiplos documentos podem estar abertos.
            if (!e.GetDocument().Equals(doc)) return;
            foreach (var id in e.GetAddedElementIds())    createdIds.Add(id.Value);
            foreach (var id in e.GetModifiedElementIds()) modifiedIds.Add(id.Value);
            foreach (var id in e.GetDeletedElementIds())  deletedIds.Add(id.Value);
        }

        AddinLog.Info("RunSafe: inscrevendo Application.DocumentChanged (rastreamento Fase 3B).");
        app.Application.DocumentChanged += OnDocumentChanged;

        try
        {
            using var tx = new Transaction(doc, txName);

            AddinLog.Info($"RunSafe: iniciando Transaction '{txName}'.");
            try { tx.Start(); }
            catch (Exception ex)
            {
                throw new TransactionFailedException($"Não foi possível iniciar a transação: {ex.Message}");
            }

            try
            {
                // THREADING CRÍTICO:
                // NÃO usar Task.Run aqui. Execute() já roda na Thread 1 (ExternalEvent handler).
                // Task.Run moveria a execução do script para o ThreadPool: leituras passam, mas
                // qualquer write no documento lança "Cannot modify the document".
                //
                // .GetAwaiter().GetResult() bloqueia a Thread 1 brevemente enquanto o Roslyn
                // compila/executa. Não causa deadlock: HandleExecuteCodeAsync (lado WS) aguarda
                // o TCS em thread de background (ThreadPool) — Thread 1 está livre para bloquear.
                AddinLog.Info(
                    $"RunSafe: iniciando execução Roslyn. " +
                    $"Thread={Environment.CurrentManagedThreadId} (deve ser Thread 1).");

                ScriptState<object> state;
                try
                {
                    state = CSharpScript
                        .RunAsync<object>(code, _scriptOptions, globals, typeof(ScriptGlobals))
                        .GetAwaiter()
                        .GetResult();
                }
                catch (CompilationErrorException cee)
                {
                    var diags = string.Join("\n", cee.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d =>
                        {
                            var pos = d.Location.GetLineSpan();
                            return $"{d.Id} ({pos.StartLinePosition.Line + 1},{pos.StartLinePosition.Character + 1}): {d.GetMessage()}";
                        }));
                    AddinLog.Error($"RunSafe: CompilationErrorException:\n{diags}");
                    throw new CompilationException(diags);
                }
                catch (AggregateException agg) when (agg.InnerException != null)
                {
                    AddinLog.Error($"RunSafe: AggregateException: {agg.InnerException.GetType().Name}: {agg.InnerException.Message}");
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo
                        .Capture(agg.InnerException).Throw();
                    throw; // unreachable
                }

                AddinLog.Info(
                    $"RunSafe: script OK. " +
                    $"Thread={Environment.CurrentManagedThreadId} (deve ser Thread 1). " +
                    $"Fazendo commit da Transaction.");

                // DocumentChanged dispara aqui, ainda na Thread 1, dentro de Commit().
                var status = tx.Commit();
                if (status != TransactionStatus.Committed)
                    throw new TransactionFailedException($"Commit retornou status: {status}");

                LastTransactionName = txName;
                AddinLog.Info(
                    $"RunSafe: committed '{txName}'. " +
                    $"ReturnValue tipo={state.ReturnValue?.GetType().Name ?? "null"}. " +
                    $"Elementos — Created={createdIds.Count}, Modified={modifiedIds.Count}, Deleted={deletedIds.Count}.");

                return new ExecuteCodeResult(
                    ReturnValue:      ToJsonSafe(state.ReturnValue),
                    Log:              logLines,
                    TransactionName:  txName,
                    ElementsCreated:  createdIds,
                    ElementsModified: modifiedIds,
                    ElementsDeleted:  deletedIds
                );
            }
            catch
            {
                // Rollback: DocumentChanged NÃO dispara → listas permanecem vazias. ✓
                AddinLog.Warn("RunSafe: rollback da Transaction. Listas de elementos permanecem vazias.");
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                throw;
            }
        }
        finally
        {
            // Sempre desincreve — sucesso, rollback, CompilationError, ou qualquer exceção.
            app.Application.DocumentChanged -= OnDocumentChanged;
            AddinLog.Info("RunSafe: desinscrevendo Application.DocumentChanged.");
        }
    }

    // ── Modo Direto (stub — Fase 3D) ─────────────────────────────────────────

    private ExecuteCodeResult RunDirect(UIApplication app, string code, List<string> logLines)
    {
        // TODO Fase 3D: executar sem Transaction do harness.
        return RunSafe(app, code, logLines);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ScriptOptions BuildScriptOptions()
    {
        return ScriptOptions.Default
            .AddReferences(
                typeof(object).Assembly,
                typeof(Enumerable).Assembly,
                typeof(List<>).Assembly,
                typeof(Autodesk.Revit.DB.Document).Assembly,
                typeof(Autodesk.Revit.UI.UIDocument).Assembly
            )
            .AddImports(
                "System",
                "System.Linq",
                "System.Collections.Generic",
                "Autodesk.Revit.DB",
                "Autodesk.Revit.DB.Architecture",
                "Autodesk.Revit.UI"
            );
    }

    private static object? ToJsonSafe(object? value)
    {
        return value switch
        {
            null                                               => null,
            bool or int or long or double or float or decimal => value,
            string s                                          => s,
            ElementId eid                                     => eid.Value,
            IEnumerable e                                     => e.Cast<object>()
                                                                   .Select(ToJsonSafe).ToList(),
            _                                                 => value.ToString()
        };
    }

    private static string SummarizeCode(string code)
    {
        var line = code
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0 && !l.StartsWith("//") && !l.StartsWith("/*"))
            ?? "execução";
        return line.Length > 60 ? line[..60] + "…" : line;
    }

    private static string TruncateCode(string code, int maxLen)
    {
        var oneLine = code.Replace('\r', ' ').Replace('\n', ' ');
        return oneLine.Length > maxLen ? oneLine[..maxLen] + "…" : oneLine;
    }

    /// <summary>
    /// Formata diagnóstico completo de uma exceção para o log:
    /// tipo, mensagem, stack trace, inner exceptions, e LoaderExceptions
    /// (para ReflectionTypeLoadException).
    /// </summary>
    internal static string BuildExceptionDiag(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Tipo: {ex.GetType().FullName}");
        sb.AppendLine($"Mensagem: {ex.Message}");
        if (ex.StackTrace != null)
        {
            sb.AppendLine("StackTrace:");
            sb.AppendLine(ex.StackTrace);
        }
        if (ex is ReflectionTypeLoadException rtle)
        {
            sb.AppendLine("LoaderExceptions:");
            foreach (var le in rtle.LoaderExceptions.Where(e => e != null).Take(10))
                sb.AppendLine($"  {le!.GetType().Name}: {le.Message}");
        }
        var inner = ex.InnerException;
        var depth = 0;
        while (inner != null && depth++ < 5)
        {
            sb.AppendLine($"InnerException[{depth}]: {inner.GetType().FullName}: {inner.Message}");
            if (inner.StackTrace != null)
                sb.AppendLine(inner.StackTrace);
            inner = inner.InnerException;
        }
        return sb.ToString().TrimEnd();
    }
}

// ── ExecuteJob ────────────────────────────────────────────────────────────────

internal sealed class ExecuteJob
{
    internal string                                    JobId  { get; }
    internal ExecuteCodeParams                         Params { get; }
    internal CancellationToken                         Ct     { get; }
    internal TaskCompletionSource<ExecuteCodeResult>   Tcs    { get; } = new();

    internal ExecuteJob(ExecuteCodeParams p, CancellationToken ct)
    {
        JobId  = Guid.NewGuid().ToString("N")[..8];
        Params = p;
        Ct     = ct;
    }
}
