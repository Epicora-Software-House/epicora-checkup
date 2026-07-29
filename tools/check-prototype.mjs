#!/usr/bin/env node
// Verificações estáticas sobre o protótipo PowerShell.
//
// NÃO É UM PARSER DE POWERSHELL. Sintaxe só é validada de verdade rodando na máquina
// Windows. O que este script faz é o que dá para fazer do Mac, e é o que mais importa:
// garantir que as proibições absolutas continuem valendo e que nada de PowerShell 7
// entre num script cujo propósito é rodar em 5.1 sem instalar nada.
//
// Entra no CI na Fase 3.

import { readFileSync } from 'node:fs';
import { join, dirname, basename } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const FILES = ['tools/prototype/Invoke-EpicoraCheckup.ps1', 'tools/prototype/Test-DataSources.ps1'];

const red = (s) => `\x1b[31m${s}\x1b[0m`;
const green = (s) => `\x1b[32m${s}\x1b[0m`;
const dim = (s) => `\x1b[2m${s}\x1b[0m`;

const problems = [];

/** Remove comentários de linha e blocos <# #> para as checagens que precisam de código real. */
const stripComments = (src) =>
  src.replace(/<#[\s\S]*?#>/g, '').split('\n').map((l) => l.replace(/(^|[^`])#.*$/, '$1')).join('\n');

// ---------------------------------------------------------------- proibições absolutas
//
// Doc 01 §7.1 e doc 02 §4.7. Cada padrão mira o USO, não a menção — os scripts
// comentam explicitamente por que não fazem essas coisas, e comentário não é violação.

const FORBIDDEN = [
  [/Win32_Product\b/, 'Win32_Product dispara reconfiguração de pacotes MSI na máquina do cliente'],
  [/LogName\s*=\s*'?Security'?/i, 'canal Security do Event Log está fora do escopo de privacidade'],
  [/\bId\s*=\s*(4624|4625|4634|4647|4648)\b/, 'evento de logon de usuário está fora do escopo de privacidade'],
  // PartialProductKey na LISTA do SELECT (entre SELECT e FROM) é leitura de credencial.
  // Na cláusula WHERE é apenas filtro, e é assim que se identifica a licença do Windows.
  [/SELECT\s+(?:(?!\bFROM\b)[\s\S])*?PartialProductKey/i, 'PartialProductKey na lista do SELECT é leitura de fragmento de credencial'],
  [/=\s*(?:\$\w+\.)?PartialProductKey/, 'PartialProductKey atribuído a variável'],
  [/key\s*=\s*clear/i, 'senha de Wi-Fi em texto claro'],
  [/New-Service|Register-ScheduledTask|Set-Service|New-NetFirewallRule/, 'criação de serviço, tarefa ou regra de firewall — a ferramenta não persiste'],
  [/Get-Content\s+.*(Documents|Desktop|Downloads|Documentos|Área de Trabalho)/i, 'leitura de conteúdo em pasta de usuário'],
];

// ---------------------------------------------------------------- PowerShell 7
//
// O alvo é 5.1, presente em toda instalação de Windows. Exigir PS7 anularia a razão
// de existir do fallback (ADR-009).

const PS7_ONLY = [
  [/\?\?/, 'operador ?? (null-coalescing) não existe no PowerShell 5.1'],
  [/\$\w+\?\./, 'operador ?. (null-conditional) não existe no PowerShell 5.1'],
  [/(?:^|[^|&])\s(?:&&|\|\|)\s/m, 'encadeamento && / || não existe no PowerShell 5.1'],
  [/-Parallel\b/, 'ForEach-Object -Parallel não existe no PowerShell 5.1'],
  [/\[\s*ValidateNotNullOrWhiteSpace\s*\]/, 'ValidateNotNullOrWhiteSpace não existe no PowerShell 5.1'],
];

for (const rel of FILES) {
  const name = basename(rel);
  const src = readFileSync(join(ROOT, rel), 'utf8');
  const code = stripComments(src);

  for (const [re, why] of FORBIDDEN) {
    const line = code.split('\n').find((l) => re.test(l));
    if (line) problems.push(`${name}: ${why}\n      ${line.trim().slice(0, 100)}`);
  }
  for (const [re, why] of PS7_ONLY) {
    const line = code.split('\n').find((l) => re.test(l));
    if (line) problems.push(`${name}: ${why}\n      ${line.trim().slice(0, 100)}`);
  }

  // ConvertTo-Json sem -Depth: o padrão do 5.1 é 2 e trunca em silêncio.
  for (const line of code.split('\n')) {
    if (/ConvertTo-Json/.test(line) && !/-Depth/.test(line)) {
      problems.push(`${name}: ConvertTo-Json sem -Depth trunca em silêncio no PowerShell 5.1\n      ${line.trim()}`);
    }
  }

  // Balanceamento de chaves. Aproximado — remove strings literais antes de contar.
  const noStrings = code
    .replace(/@"[\s\S]*?"@/g, '""').replace(/@'[\s\S]*?'@/g, "''")
    .replace(/'(?:[^']|'')*'/g, "''");
  const open = (noStrings.match(/\{/g) || []).length;
  const close = (noStrings.match(/\}/g) || []).length;
  if (open !== close) {
    problems.push(`${name}: chaves desbalanceadas — ${open} abrem, ${close} fecham (contagem aproximada)`);
  }
}

// ---------------------------------------------------------------- cobertura de coletores

const schema = JSON.parse(readFileSync(join(ROOT, 'schema', 'checkup-1.0.schema.json'), 'utf8'));
const schemaIds = schema.$defs.collectorId.enum;
const main = readFileSync(join(ROOT, 'tools', 'prototype', 'Invoke-EpicoraCheckup.ps1'), 'utf8');
const implemented = [...main.matchAll(/Invoke-Collector -Id '([a-z0-9]+)'/g)].map((m) => m[1]);

for (const id of implemented) {
  if (!schemaIds.includes(id)) problems.push(`coletor "${id}" não consta do enum collectorId do schema`);
}
for (const id of schemaIds) {
  if (!implemented.includes(id)) problems.push(`coletor "${id}" existe no schema mas não foi implementado no protótipo`);
}
const dup = implemented.filter((v, i) => implemented.indexOf(v) !== i);
if (dup.length) problems.push(`coletores duplicados no protótipo: ${[...new Set(dup)].join(', ')}`);

// ---------------------------------------------------------------- relatório

console.log(`coletores: ${implemented.length}/${schemaIds.length} implementados`);
console.log(dim(`  ${implemented.join(', ')}`));
console.log('');

if (problems.length) {
  for (const p of problems) console.log(`${red('✗')} ${p}`);
  console.log('');
  console.log(red(`${problems.length} problema(s).`));
  process.exit(1);
}

console.log(green('Verificações estáticas passaram.'));
console.log(dim('Isto NÃO é um parser de PowerShell. A sintaxe só é validada rodando na máquina Windows.'));
