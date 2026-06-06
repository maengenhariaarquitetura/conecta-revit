// TODO Fase 4: implementar loader de skill packs.
//
// Responsabilidade (ARCHITECTURE § 7):
//   - Varrer duas pastas de skills:
//       (1) <bridge_dir>/skills/       — skills embarcadas na distribuição
//       (2) %AppData%\ConectaRevit\skills\  — skills instaladas pelo usuário
//   - Para cada pasta <nome>/: ler skill.json e instructions.md.
//   - Registrar cada skill como MCP prompt "skill_<nome>" com o instructions.md como contexto.
//   - Logar skills carregadas na inicialização.
