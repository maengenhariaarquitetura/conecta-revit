using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ConectaRevit.Addin.Execution;

/// <summary>
/// Variáveis globais injetadas no contexto do script Roslyn (ARCHITECTURE § 5.5, PROTOCOL.md § 3.4).
///
/// DEVE ser public (classe E membros): o Roslyn compila o script em um assembly separado
/// ("Submission#0") que recebe os globals via construtor. Se a classe ou qualquer membro
/// acessado pelo script for internal, o CLR lança TypeAccessException ao instanciar o
/// submission — mesmo que o assembly do add-in seja o próprio host do script.
///
/// Uso no script:
///   var walls = new FilteredElementCollector(Doc).OfClass(typeof(Wall)).ToElements();
///   Log($"Encontrei {walls.Count} paredes");
///   return walls.Count;
///
/// TODO Fase 3D: adicionar o helper Tools (IRevitTool registry).
/// </summary>
public sealed class ScriptGlobals
{
    /// <summary>
    /// Documento Revit ativo.
    /// Nunca null — verificado antes de executar; lança NO_DOCUMENT se ausente.
    /// </summary>
    public Document Doc { get; init; } = null!;

    /// <summary>UIDocument ativo (acesso a View, seleção, etc.).</summary>
    public UIDocument UiDoc { get; init; } = null!;

    /// <summary>
    /// UIApplication do Revit.
    /// Permite acessar Application, PostCommand, OpenAndActivateDocument, etc.
    /// </summary>
    public UIApplication UiApp { get; init; } = null!;

    /// <summary>
    /// Captura mensagens de log do script para o resultado.
    /// Use: Log("mensagem");
    /// As linhas são devolvidas em ExecuteCodeResult.Log[].
    /// </summary>
    public Action<string> Log { get; init; } = null!;
}
