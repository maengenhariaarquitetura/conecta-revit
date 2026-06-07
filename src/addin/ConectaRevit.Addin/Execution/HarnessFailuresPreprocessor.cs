using Autodesk.Revit.DB;
using ConectaRevit.Addin.Logging;

namespace ConectaRevit.Addin.Execution;

/// <summary>
/// Pré-processador de falhas do harness em Modo Seguro.
///
/// Objetivo principal: NUNCA deixar o Revit abrir um diálogo modal durante a execução
/// de um job — o harness roda síncrono na Thread 1 e qualquer diálogo trava a ponte MCP.
///
/// Comportamento por severidade:
///   • <see cref="FailureSeverity.Warning"/>: suprimido via DeleteWarning + coletado em log[].
///   • Error com resolução disponível: resolvido automaticamente (primeiro candidato) + log[].
///   • Error sem resolução: rollback imediato (ProceedWithRollBack) + log[], sem diálogo.
///
/// Configurado na Transaction do harness via FailureHandlingOptions antes de tx.Start().
/// </summary>
internal sealed class HarnessFailuresPreprocessor : IFailuresPreprocessor
{
    private readonly List<string> _logLines;

    internal HarnessFailuresPreprocessor(List<string> logLines)
        => _logLines = logLines;

    public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
    {
        try
        {
            // ToList() cria cópia — DeleteWarning/ResolveFailure modificam a coleção interna.
            var failures = failuresAccessor.GetFailureMessages().ToList();
            if (failures.Count == 0)
                return FailureProcessingResult.Continue;

            AddinLog.Info($"HarnessFailuresPreprocessor: {failures.Count} falha(s) recebida(s).");

            foreach (var failure in failures)
            {
                var severity    = failure.GetSeverity();
                var description = failure.GetDescriptionText();

                if (severity == FailureSeverity.Warning)
                {
                    // Warnings: suprimir sem rollback — operação prossegue.
                    // Mensagem vai para log[] do resultado para informar o usuário.
                    var logMsg = $"[Revit Warning] {description}";
                    _logLines.Add(logMsg);
                    AddinLog.Warn($"Preprocessor: suprimindo warning — {description}");
                    failuresAccessor.DeleteWarning(failure);
                }
                else
                {
                    // Error (ou mais grave): tentar resolução automática com a resolução padrão.
                    // API Revit 2026: HasResolutions() indica se existe alguma resolução;
                    // GetCurrentResolutionType() retorna o tipo padrão; ResolveFailure() aplica.
                    // Não existe GetApplicableResolutionTypes() nesta versão da API.
                    if (failure.HasResolutions())
                    {
                        var resType = failure.GetCurrentResolutionType();
                        failuresAccessor.ResolveFailure(failure);
                        var logMsg = $"[Revit Error auto-resolvido ({resType})] {description}";
                        _logLines.Add(logMsg);
                        AddinLog.Warn($"Preprocessor: erro auto-resolvido ({resType}) — {description}");
                    }
                    else
                    {
                        // Erro não resolvível: rollback imediato, sem diálogo.
                        var logMsg = $"[Revit Error — rollback automático] {description}";
                        _logLines.Add(logMsg);
                        AddinLog.Error($"Preprocessor: erro não resolvível → ProceedWithRollBack — {description}");
                        return FailureProcessingResult.ProceedWithRollBack;
                    }
                }
            }

            // Todos os problemas foram tratados: commit pode prosseguir.
            return FailureProcessingResult.ProceedWithCommit;
        }
        catch (Exception ex)
        {
            // Exceção inesperada no preprocessor: rollback é mais seguro que commit cego.
            AddinLog.Error($"HarnessFailuresPreprocessor: exceção inesperada: {ex.GetType().Name}: {ex.Message}. Rollback preventivo.");
            return FailureProcessingResult.ProceedWithRollBack;
        }
    }
}
