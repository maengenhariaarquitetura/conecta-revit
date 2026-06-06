using Autodesk.Revit.UI;

namespace ConectaRevit.Addin.Execution;

// Motor de execução na thread principal do Revit via ExternalEvent (ARCHITECTURE § 5.3).
// TODO Fase 3: implementar fila de jobs, ExternalEvent e TaskCompletionSource.
//
// Padrão obrigatório:
//   WS recebe request (thread bg)
//     → enfileira job + cria TaskCompletionSource
//     → externalEvent.Raise()
//   Revit chama IExternalEventHandler.Execute(UIApplication) na thread principal
//     → desenfileira job, executa, resolve TaskCompletionSource
//   WS awaita o TCS e devolve resposta
//
// Regras:
//   - ExternalEvent.Create() ocorre em Application.OnStartup (contexto de API válido).
//   - Um único ExternalEvent/handler com fila interna — não criar por requisição.
//   - Modo Seguro: harness abre TransactionGroup antes do código, rollback em exceção.
//   - Modo Direto: código gerencia suas próprias transações (ARCHITECTURE § 5.4).
internal sealed class ExecutionEngine
{
    // TODO Fase 3: implementar Initialize(UIApplication), EnqueueJob, e o IExternalEventHandler.
    public void Initialize(UIApplication uiApp) { }
}
