#!/usr/bin/env node
// Valida todos os JSONs de tests/fixtures/ contra schema/checkup-1.0.schema.json.
// Roda no Mac do analista. Entra no CI na Fase 3.

import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { join, dirname, basename } from 'node:path';
import { fileURLToPath } from 'node:url';
import Ajv from 'ajv/dist/2020.js';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const SCHEMA = join(ROOT, 'schema', 'checkup-1.0.schema.json');
const FIXTURES = join(ROOT, 'tests', 'fixtures');

const red = (s) => `\x1b[31m${s}\x1b[0m`;
const green = (s) => `\x1b[32m${s}\x1b[0m`;
const dim = (s) => `\x1b[2m${s}\x1b[0m`;

const ajv = new Ajv({ allErrors: true, strict: false });
const validate = ajv.compile(JSON.parse(readFileSync(SCHEMA, 'utf8')));

const targets = process.argv.slice(2).length
  ? process.argv.slice(2)
  : existsSync(FIXTURES)
    ? readdirSync(FIXTURES).filter((f) => f.endsWith('.json')).map((f) => join(FIXTURES, f))
    : [];

if (!targets.length) {
  console.log(dim('Nenhuma fixture encontrada em tests/fixtures/.'));
  process.exit(0);
}

let failed = 0;

for (const file of targets) {
  let doc;
  try {
    doc = JSON.parse(readFileSync(file, 'utf8'));
  } catch (err) {
    console.log(`${red('✗')} ${basename(file)} — JSON inválido: ${err.message}`);
    failed++;
    continue;
  }

  if (validate(doc)) {
    console.log(`${green('✓')} ${basename(file)}`);
    continue;
  }

  failed++;
  console.log(`${red('✗')} ${basename(file)}`);
  // O despacho por coletor usa oneOf + if/then, que gera um erro real mais três de
  // encadeamento. Só o real localiza o problema; o resto é ruído.
  const noise = /^(must be null|must match exactly one schema in oneOf|must match "(then|else)" schema)$/;
  const seen = new Set();
  for (const e of validate.errors) {
    if (noise.test(e.message)) continue;
    const path = e.instancePath || '(raiz)';
    const extra = e.params?.additionalProperty ? ` → "${e.params.additionalProperty}"` : '';
    const line = `    ${path} ${e.message}${extra}`;
    if (seen.has(line)) continue;
    seen.add(line);
    console.log(line);
  }
}

console.log('');
if (failed) {
  console.log(red(`${failed} de ${targets.length} arquivo(s) fora do schema.`));
  process.exit(1);
}
console.log(green(`${targets.length} arquivo(s) válidos contra o schema 1.0.`));
