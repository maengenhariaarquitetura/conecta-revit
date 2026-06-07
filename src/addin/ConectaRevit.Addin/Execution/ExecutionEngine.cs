using System.Collections.Concurrent;
using Autodesk.Revit.UI;
using ConectaRevit.Shared;

namespace ConectaRevit.Addin.Execution;

// Motor de execução na thread principal via ExternalEvent + TCS (ARCHITECTURE § 5.3).
//
// Padrão de fluxo:
//   WS recebe request  →  EnqueueAsync cria job + TCS  →  externalEvent.Raise()
//   Revit chama Execute() na thread principal  →  job executado  →  TCS resolvido
//   WS awaita TCS e envia resposta
//
// Fase 2: stub que ignora o code e devolve "Revit {version}" (PROTOCOL.md § 7).
// Fase 3: substituir stub por execução real via Roslyn.
internal sealed class ExecutionEngine : IExternalEventHandler
{
    private readonly string _revitVersion;         // cache do OnStartup — seguro de ler em qualquer thread
    private ExternalEvent? _externalEvent;

    // Fila serial: processado um job por chamada a Execute() (ARCHITECTURE § 5.2).
    // Capacidade máxima visível para o servidor WS: 10 jobs (acima → BUSY).
    private readonly ConcurrentQueue<ExecuteJob> _queue = new();

    internal ExecutionEngine(string revitVersion) => _revitVersion = revitVersion;

    internal void SetExternalEvent(ExternalEvent ev) => _externalEvent = ev;

    // Enfileira um execute_code e retorna Task que resolve quando o job terminar.
    // ct deve ter timeout de 120 s (criado pelo WebSocketServer).
    internal Task<ExecuteCodeResult> EnqueueAsync(ExecuteCodeParams p, CancellationToken ct)
    {
        if (_queue.Count >= 10)
            throw new InvalidOperationException("BUSY");

        var job = new ExecuteJob(p, ct);
        _queue.Enqueue(job);
        _externalEvent!.Raise();

        // WaitAsync propaga o CancellationToken (timeout) sem cancelar o TCS internamente.
        return job.Tcs.Task.WaitAsync(ct);
    }

    // Chamado pelo Revit na thread principal.
    public void Execute(UIApplication app)
    {
        if (!_queue.TryDequeue(out var job)) return;

        // Job cancelado antes de chegar à thread principal (timeout do WS).
        if (job.Ct.IsCancellationRequested)
        {
            job.Tcs.TrySetCanceled(job.Ct);
        }
        else
        {
            try
            {
                // Fase 2 STUB: ignora job.Params.Code, devolve versão do Revit.
                // TODO Fase 3: executar job.Params.Code via CSharpScript (Roslyn).
                var result = new ExecuteCodeResult(
                    ReturnValue: $"Revit {_revitVersion}",
                    Log: [],
                    TransactionName: null,
                    ElementsCreated: []
                );
                job.Tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                job.Tcs.TrySetException(ex);
            }
        }

        // Se há mais jobs na fila, aciona novamente para processamento serial.
        if (!_queue.IsEmpty)
            _externalEvent!.Raise();
    }

    public string GetName() => "ConectaRevit.ExecutionEngine";
}

// Job enfileirado: contém os params, o token de cancelamento (timeout) e a TCS de resposta.
// internal (não file): ConcurrentQueue<ExecuteJob> é membro de ExecutionEngine — C# não permite
// usar tipo file-local como argumento de tipo em membros de tipos não-file-local (CS9051).
// ExecuteJob é detalhe interno do add-in; não pertence ao shared/.
internal sealed class ExecuteJob
{
    internal ExecuteCodeParams    Params { get; }
    internal CancellationToken    Ct     { get; }
    internal TaskCompletionSource<ExecuteCodeResult> Tcs { get; } = new();

    internal ExecuteJob(ExecuteCodeParams p, CancellationToken ct) { Params = p; Ct = ct; }
}
