using Autodesk.Revit.UI;

namespace ConectaRevit.Addin.Ribbon;

// Cria e gerencia o painel "ConectaRevit" com os 6 botões do ribbon (ARCHITECTURE § 5.1).
// TODO Fase 3: implementar criação dos botões via UIControlledApplication.CreateRibbonPanel.
//
// Botões:
//   1. Conectar / Desconectar — liga/desliga servidor WS; valida licença antes de ligar.
//   2. Status                 — exibe estado: ligado/desligado, porta, documento ativo.
//   3. Verificar Requisitos   — checa Claude Desktop, assinatura, porta livre, .mcpb registrado.
//   4. Console                — janela com log em tempo real do que o Claude executou.
//   5. Reverter última ação   — desfaz última ação do Claude via Undo nativo do Revit.
//   6. Configurações          — switch Modo Seguro/Direto, porta, chave de licença.
internal sealed class RibbonPanel
{
    // TODO Fase 3: implementar Register criando os 6 PushButtonData e adicionando ao painel.
    public void Register(UIControlledApplication application) { }
}
