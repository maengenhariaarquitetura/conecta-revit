// TODO Fase 4: registrar as 5 tools MCP e os prompts de skills no servidor MCP.
//
// Tools a expor (ARCHITECTURE § 6):
//   - revit_execute_code  → PROTOCOL.md § 3.4 execute_code
//   - revit_get_context   → PROTOCOL.md § 3.3 get_context
//   - revit_run_tool      → PROTOCOL.md § 3.5 run_tool
//   - revit_list_tools    → PROTOCOL.md § 3.6 list_tools
//   - revit_revert_last   → PROTOCOL.md § 3.7 revert_last
//
// Prompts: um MCP prompt por skill carregada (skill_<nome>).
// Cada tool translada a chamada MCP em um Request para o RevitClient
// e aguarda a Response correlacionada.
