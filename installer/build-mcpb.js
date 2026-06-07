'use strict';
/**
 * Empacota a ponte como .mcpb autocontido para distribuição via Claude Desktop Extensions.
 *
 * Sequência:
 *   1. npm run build  (prebuild: copia shared/protocol.ts + tsc)
 *   2. Cria dist/mcpb-staging/ com os arquivos JS compilados + manifest.json
 *   3. npm install --omit=dev no staging  → node_modules de produção apenas
 *   4. mcpb validate  → confirma manifest antes de empacotar
 *   5. mcpb pack staging/ → ConectaRevit-<version>.mcpb em dist/
 *
 * Execução:
 *   node installer/build-mcpb.js        (da raiz do repositório)
 *   npm run pack:mcpb                   (de src/bridge/)
 */

const { execSync } = require('child_process');
const fs   = require('fs');
const path = require('path');

// ── Caminhos ──────────────────────────────────────────────────────────────────
const repoRoot   = path.resolve(__dirname, '..');
const bridgeDir  = path.join(repoRoot, 'src', 'bridge');
const distRoot   = path.join(repoRoot, 'dist');
const stagingDir = path.join(distRoot, 'mcpb-staging');
const version    = fs.readFileSync(path.join(repoRoot, 'VERSION'), 'utf8').trim();
const outputFile = path.join(distRoot, `ConectaRevit-${version}.mcpb`);

// ── 1. Build TypeScript ──────────────────────────────────────────────────────
log('1/5  Building bridge (prebuild + tsc)…');
execSync('npm run build', { cwd: bridgeDir, stdio: 'inherit' });

// ── 2. Preparar staging ──────────────────────────────────────────────────────
log('2/5  Preparing staging directory…');
fs.rmSync(stagingDir, { recursive: true, force: true });
fs.mkdirSync(stagingDir, { recursive: true });

// Copia dist/ (JS compilado) para a raiz do staging — bate com entry_point "index.js".
copyDir(path.join(bridgeDir, 'dist'), stagingDir);

// Copia manifest.json (fonte: src/bridge/manifest.json).
fs.copyFileSync(
  path.join(bridgeDir, 'manifest.json'),
  path.join(stagingDir, 'manifest.json')
);

// ── 3. Instalar dependências de produção no staging ──────────────────────────
// O .mcpb precisa ser autocontido: node_modules embarcados, sem npm install na máquina
// do cliente. Instala apenas prod deps (omit=dev exclui TypeScript, @types/*, etc.).
log('3/5  Installing production dependencies into staging…');
const pkg = JSON.parse(fs.readFileSync(path.join(bridgeDir, 'package.json'), 'utf8'));
fs.writeFileSync(
  path.join(stagingDir, 'package.json'),
  JSON.stringify(
    { name: pkg.name, version: pkg.version, dependencies: pkg.dependencies },
    null,
    2
  )
);
execSync('npm install --omit=dev --no-package-lock', {
  cwd: stagingDir,
  stdio: 'inherit',
});

// ── 4. Validar manifest ──────────────────────────────────────────────────────
log('4/5  Validating manifest…');
execSync(
  `npx --yes @anthropic-ai/mcpb validate "${path.join(stagingDir, 'manifest.json')}"`,
  { cwd: repoRoot, stdio: 'inherit' }
);

// ── 5. Empacotar ─────────────────────────────────────────────────────────────
log(`5/5  Packing → ${path.relative(repoRoot, outputFile)}`);
fs.mkdirSync(distRoot, { recursive: true });
execSync(
  `npx @anthropic-ai/mcpb pack "${stagingDir}" "${outputFile}"`,
  { cwd: repoRoot, stdio: 'inherit' }
);

log(`✓  Gerado: ${path.relative(repoRoot, outputFile)}`);

// ── Helpers ───────────────────────────────────────────────────────────────────
function log(msg) { console.log(`[build-mcpb] ${msg}`); }

function copyDir(src, dest) {
  fs.mkdirSync(dest, { recursive: true });
  for (const entry of fs.readdirSync(src, { withFileTypes: true })) {
    const s = path.join(src, entry.name);
    const d = path.join(dest, entry.name);
    if (entry.isDirectory()) copyDir(s, d);
    else fs.copyFileSync(s, d);
  }
}
