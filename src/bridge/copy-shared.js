#!/usr/bin/env node
/**
 * Pré-build: copia shared/protocol.ts para src/shared/protocol.ts antes do tsc.
 *
 * Por que existe: rootDir do tsconfig é "src" (src/bridge/src); importar de fora
 * desse diretório viola TS6059. A cópia local mantém shared/protocol.ts como
 * fonte da verdade sem alterar o rootDir.
 *
 * O arquivo gerado (src/shared/protocol.ts) é listado no .gitignore e não deve
 * ser editado diretamente — edite sempre shared/protocol.ts na raiz do repositório.
 */
'use strict';

const fs   = require('fs');
const path = require('path');

// __dirname = <repo>/src/bridge  (onde fica este script)
const repoRoot = path.resolve(__dirname, '..', '..');          // src/bridge → src → raiz
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
