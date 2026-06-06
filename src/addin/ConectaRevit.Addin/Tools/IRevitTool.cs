namespace ConectaRevit.Addin.Tools;

// Interface das ferramentas de alto nível pré-compiladas (ARCHITECTURE § 5.6).
// TODO Fase 3: implementar ferramentas MVP — create_wall, get_selection_info,
//              set_parameter, list_categories — e o ToolRegistry (registro por reflection).
//
// As tools são expostas de duas formas (ARCHITECTURE § 5.6):
//   (a) Como tool MCP revit_run_tool (Claude chama sem escrever código).
//   (b) Acessíveis de dentro do código Roslyn via o helper global Tools.
public interface IRevitTool
{
    string Name { get; }
    string Description { get; }

    // TODO Fase 3: substituir por System.Text.Json.Nodes.JsonObject (schema JSON real).
    object InputSchema { get; }

    // TODO Fase 3: substituir pelos tipos ToolContext e JsonObject definitivos.
    object Execute(object ctx, object args);
}
