#!/usr/bin/env node
/**
 * Pré-build:
 *   1. Copia shared/protocol.ts → src/shared/protocol.ts (antes do tsc).
 *   2. Copia skills/ → dist/skills-builtin/ (skills embarcadas no pacote).
 *
 * Por que (1) existe: rootDir do tsconfig é "src"; importar de fora viola TS6059.
 * A cópia local mantém shared/protocol.ts como fonte da verdade.
 *
 * Por que (2) existe: o tsc não copia arquivos não-TS. As skills (skill.json +
 * instructions.md) precisam chegar a dist/ para serem empacotadas no .mcpb e
 * carregadas em runtime via __dirname relativo ao loader compilado.
 *
 * O arquivo src/shared/protocol.ts é listado no .gitignore — edite sempre
 * shared/protocol.ts na raiz do repositório.
 */
'use strict';

const fs   = require('fs');
const path = require('path');

// __dirname = <repo>/src/bridge  (onde fica este script)
const repoRoot = path.resolve(__dirname, '..', '..');          // src/bridge → src → raiz

// ── 1. shared/protocol.ts ─────────────────────────────────────────────────────
const src      = path.join(repoRoot, 'shared', 'protocol.ts');
const destDir  = path.join(__dirname, 'src', 'shared');        // src/bridge/src/shared/
const dest     = path.join(destDir, 'protocol.ts');

if (!fs.existsSync(src)) {
  console.error(`[copy-shared] ERRO: fonte não encontrada: ${src}`);
  process.exit(1);
}

fs.mkdirSync(destDir, { recursive: true });
fs.copyFileSync(src, dest);
console.log(`[copy-shared] ${path.relative(repoRoot, src)} → ${path.relative(repoRoot, dest)}`);

// ── 2. skills/ → dist/skills-builtin/ ────────────────────────────────────────
// Fonte:  src/bridge/skills/         (skills embarcadas, versionadas no git)
// Destino: src/bridge/dist/skills-builtin/   (copiado integralmente pelo build-mcpb.js)
// Runtime: loader resolve via path.join(__dirname, '..', 'skills-builtin')
//          sendo __dirname = <package>/skills/ (onde fica o skills/index.js compilado).
const skillsSrc  = path.join(__dirname, 'skills');             // src/bridge/skills/
const skillsDest = path.join(__dirname, 'dist', 'skills-builtin');

if (fs.existsSync(skillsSrc)) {
  copyDir(skillsSrc, skillsDest);
  const count = fs.readdirSync(skillsSrc, { withFileTypes: true })
    .filter(e => e.isDirectory()).length;
  console.log(`[copy-shared] skills/ → dist/skills-builtin/ (${count} skill(s))`);
} else {
  // Pasta ainda não criada (repo limpo): cria o destino vazio para não quebrar o loader.
  fs.mkdirSync(skillsDest, { recursive: true });
  console.log('[copy-shared] skills/ não encontrado — dist/skills-builtin/ criado vazio.');
}

// ── Helper ────────────────────────────────────────────────────────────────────
function copyDir(src, dest) {
  fs.mkdirSync(dest, { recursive: true });
  for (const entry of fs.readdirSync(src, { withFileTypes: true })) {
    const s = path.join(src, entry.name);
    const d = path.join(dest, entry.name);
    if (entry.isDirectory()) copyDir(s, d);
    else fs.copyFileSync(s, d);
  }
}
