// TODO Fase 2: implementar inicialização completa da ponte MCP.
//
// Responsabilidade (ARCHITECTURE § 6):
//   1. Ler %AppData%\ConectaRevit\runtime.json para descobrir a porta do add-in.
//   2. Instanciar RevitClient e abrir conexão WebSocket com o add-in.
//   3. Executar handshake e validar protocolVersion (MAJOR incompatível = recusar).
//   4. Iniciar servidor MCP via stdio usando @modelcontextprotocol/sdk.
//   5. Registrar as tools MCP (mcp/index.ts) e os prompts de skills (skills/index.ts).
//   6. Reexpedir eventos log/status recebidos do add-in como notificações MCP.
//
// Timeout da ponte: 125s por requisição (PROTOCOL.md § 2 — acima dos 120s do add-in).
