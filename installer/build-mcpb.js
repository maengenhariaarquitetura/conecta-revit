// Empacota a ponte como .mcpb via @anthropic-ai/mcpb.
// Chamado por scripts/build.ps1 após `npm run build` na ponte.
// TODO Fase 4: validar que `mcpb pack` aceita o manifest.json e gera o .mcpb corretamente.

'use strict';

const { execSync } = require('child_process');
const path = require('path');

const repoRoot  = path.resolve(__dirname, '..');
const bridgeDir = path.join(repoRoot, 'src', 'bridge');
const outputDir = path.join(repoRoot, 'dist');
const manifest  = path.join(bridgeDir, 'manifest.json');

console.log(`mcpb pack ${manifest} → ${outputDir}`);

execSync(`npx mcpb pack "${manifest}" --output "${outputDir}"`, {
  stdio: 'inherit',
  cwd: bridgeDir,
});
