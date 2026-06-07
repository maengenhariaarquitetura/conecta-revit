namespace ConectaRevit.Addin.Execution;

// Exceções internas que mapeiam diretamente para os códigos de erro do PROTOCOL.md § 5.
// O WebSocketServer captura cada tipo e envia o code correto na resposta.
// Não expor ao shared/ — são detalhes de implementação do add-in.

/// <summary>
/// Código C# enviado pelo usuário não compilou.
/// Mapeado para COMPILATION_ERROR com diagnostics como details.
/// </summary>
internal sealed class CompilationException(string diagnostics)
    : Exception("Erro de compilação do código C#.")
{
    internal string Diagnostics { get; } = diagnostics;
}

/// <summary>
/// Nenhum documento Revit aberto quando execute_code foi chamado.
/// Mapeado para NO_DOCUMENT.
/// </summary>
internal sealed class NoDocumentException()
    : Exception("Nenhum documento Revit aberto. Abra um projeto antes de executar código.") { }

/// <summary>
/// Falha ao iniciar ou commitar a Transaction do harness (Modo Seguro).
/// Mapeado para TRANSACTION_FAILED.
/// </summary>
internal sealed class TransactionFailedException(string details)
    : Exception("Falha na transação do harness.")
{
    internal string Details { get; } = details;
}
