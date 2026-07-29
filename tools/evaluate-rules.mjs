#!/usr/bin/env node
// Motor de regras de REFERÊNCIA. Roda no Mac, sobre um JSON de coleta.
//
// Existe para validar a matriz contra máquinas reais antes de existir uma linha de C#.
// As fixtures com saída esperada viram o contrato de aceite do motor da Fase 2 —
// quando o C# passar em todas, este motor é aposentado. É instrumento, não segundo sistema.
//
//   node tools/evaluate-rules.mjs <arquivo.json> [--incluir-pendentes] [--json]

import { readFileSync, readdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const RULES_DIR = join(ROOT, 'rules');
const SUPPORT = new Set(['startup-exclusions.json', 'event-ids.json', 'windows-builds.json', 'win11-cpu-support.json', 'README.md']);

const args = process.argv.slice(2);
const includePending = args.includes('--incluir-pendentes');
const asJson = args.includes('--json');
const target = args.find((a) => !a.startsWith('--'));

if (!target) {
  console.error('uso: node tools/evaluate-rules.mjs <arquivo.json> [--incluir-pendentes] [--json]');
  process.exit(2);
}

const doc = JSON.parse(readFileSync(target, 'utf8'));

const rules = readdirSync(RULES_DIR)
  .filter((f) => f.endsWith('.json') && !SUPPORT.has(f))
  .sort()
  .flatMap((f) => JSON.parse(readFileSync(join(RULES_DIR, f), 'utf8')).rules);

// ---------------------------------------------------------------- leitura de valor

const MISSING = Symbol('missing');
const evaluationWarnings = [];

/** Devolve { value } ou { unavailable: 'motivo' } para um caminho pontilhado. */
function read(path) {
  const segs = path.split('.');
  let node;

  if (segs[0] === 'collectors') {
    const collector = (doc.collectors ?? []).find((c) => c.id === segs[1]);
    if (!collector) return { unavailable: `coletor "${segs[1]}" ausente da execução` };
    if (collector.status !== 'Completed') {
      const why = collector.skipReason ?? collector.errors?.[0]?.message ?? 'sem detalhe';
      const label = collector.status === 'Skipped' ? 'ignorado' : 'falhou';
      return { unavailable: `coletor "${collector.displayName}" ${label} — ${why}` };
    }
    node = collector.data;
    segs.splice(0, 3);
  } else {
    node = doc;
  }

  for (const seg of segs) {
    if (node === null || node === undefined || typeof node !== 'object') return { value: MISSING };
    if (!(seg in node)) return { value: MISSING };
    node = node[seg];
  }
  return { value: node };
}

const isNullish = (v) => v === null || v === undefined || v === MISSING;

// ---------------------------------------------------------------- operadores

const NULL_AWARE = new Set(['isNull', 'isNotNull', 'isTrue', 'isFalse', 'isEmpty', 'isNotEmpty']);

function applyOperator(op, v, expected) {
  switch (op) {
    case 'isNull': return isNullish(v);
    case 'isNotNull': return !isNullish(v);
    case 'isTrue': return v === true;
    case 'isFalse': return v === false;
    case 'isEmpty': return Array.isArray(v) && v.length === 0;
    case 'isNotEmpty': return Array.isArray(v) && v.length > 0;
    case 'equals': return v === expected;
    case 'notEquals': return v !== expected;
    case 'lessThan': return typeof v === 'number' && v < expected;
    case 'greaterThan': return typeof v === 'number' && v > expected;
    case 'contains':
      if (Array.isArray(v)) return v.includes(expected);
      return typeof v === 'string' && v.includes(expected);
    case 'notContains':
      if (Array.isArray(v)) return !v.includes(expected);
      return typeof v === 'string' && !v.includes(expected);
    case 'inList': return Array.isArray(expected) && expected.includes(v);
    case 'notInList': return Array.isArray(expected) && !expected.includes(v);
    default: throw new Error(`operador desconhecido: ${op}`);
  }
}

function evalCondition(cond, rule) {
  if (Array.isArray(cond.allOf)) return cond.allOf.every((c) => evalCondition(c, rule));
  if (Array.isArray(cond.anyOf)) return cond.anyOf.some((c) => evalCondition(c, rule));
  if (cond.not) return !evalCondition(cond.not, rule);

  const r = read(cond.path);
  const v = 'unavailable' in r ? MISSING : r.value;

  // Comparação sobre valor ausente que NÃO está em `requires` devolve false em silêncio,
  // o que vira Compliant. Às vezes é intencional (OS-004), às vezes é bug de regra.
  if (isNullish(v) && !NULL_AWARE.has(cond.operator) && !(rule.requires ?? []).includes(cond.path)) {
    evaluationWarnings.push(`${rule.id}: "${cond.path}" está nulo e não consta em requires — a comparação "${cond.operator}" resolveu falso`);
  }
  return applyOperator(cond.operator, v, cond.value);
}

// ---------------------------------------------------------------- avaliação

const findings = [];

for (const rule of rules) {
  if (!rule.enabled && !includePending) continue;

  const finding = {
    ruleId: rule.id,
    ruleVersion: rule.version,
    severity: rule.severity,
    state: null,
    indeterminateReason: null,
    weight: rule.weight,
    title: rule.title,
    clientText: rule.clientText,
    recommendedAction: rule.recommendedAction,
    evidence: null,
    linkedOptimizations: rule.linkedOptimizations ?? [],
    markedFalsePositive: false,
    falsePositiveJustification: null,
  };

  // 1. requires — caminho ausente ou coletor não concluído → Indeterminate, nunca NonCompliant
  let blocked = null;
  for (const p of rule.requires ?? []) {
    const r = read(p);
    if ('unavailable' in r) { blocked = r.unavailable; break; }
    if (isNullish(r.value)) { blocked = `dado não disponível: ${p}`; break; }
  }

  if (blocked) {
    finding.state = 'Indeterminate';
    finding.indeterminateReason = blocked;
  } else if (rule.indeterminateWhen && evalCondition(rule.indeterminateWhen, rule)) {
    // 2. indeterminateWhen — o guard que impede "Unknown" de virar Compliant em silêncio
    finding.state = 'Indeterminate';
    finding.indeterminateReason = rule.validationNote
      ? `condição de indeterminação atendida — ${rule.validationNote.split('.')[0]}`
      : 'condição de indeterminação atendida';
  } else {
    // 3. condition
    finding.state = evalCondition(rule.condition, rule) ? 'NonCompliant' : 'Compliant';
  }

  if (finding.state === 'NonCompliant') {
    const evidence = {};
    for (const p of rule.evidenceFields ?? []) {
      const r = read(p);
      if (!('unavailable' in r) && !isNullish(r.value)) evidence[p] = r.value;
    }
    if (Object.keys(evidence).length) finding.evidence = evidence;
  }

  findings.push(finding);
}

// ---------------------------------------------------------------- score e veredito

const nonCompliant = findings.filter((f) => f.state === 'NonCompliant');
const value = Math.max(0, 100 - nonCompliant.reduce((s, f) => s + (f.weight ?? 0), 0));
const band = value >= 80 ? 'Green' : value >= 50 ? 'Yellow' : 'Red';

const ruleById = new Map(rules.map((r) => [r.id, r]));
const replaceDrivers = nonCompliant.filter((f) => ruleById.get(f.ruleId)?.verdictInfluence === 'Replace');
const upgradeDrivers = nonCompliant.filter((f) => ruleById.get(f.ruleId)?.verdictInfluence === 'Upgrade');

const verdict = replaceDrivers.length ? 'Replace' : upgradeDrivers.length ? 'Upgrade' : 'Keep';
const verdictDrivenBy = (replaceDrivers.length ? replaceDrivers : upgradeDrivers).map((f) => f.ruleId);

const SEV_ORDER = { Critical: 0, High: 1, Medium: 2, Low: 3, Info: 4 };
findings.sort((a, b) => (SEV_ORDER[a.severity] - SEV_ORDER[b.severity]) || a.ruleId.localeCompare(b.ruleId));

const score = { value, band, verdict, verdictDrivenBy };

if (asJson) {
  console.log(JSON.stringify({ findings, score }, null, 2));
  process.exit(0);
}

// ---------------------------------------------------------------- relatório legível

const red = (s) => `\x1b[31m${s}\x1b[0m`;
const yellow = (s) => `\x1b[33m${s}\x1b[0m`;
const green = (s) => `\x1b[32m${s}\x1b[0m`;
const dim = (s) => `\x1b[2m${s}\x1b[0m`;
const bold = (s) => `\x1b[1m${s}\x1b[0m`;

const bandColor = { Green: green, Yellow: yellow, Red: red }[band];
const verdictPt = { Keep: 'Manter', Upgrade: 'Fazer upgrade', Replace: 'Substituir' }[verdict];

console.log('');
console.log(bold(`${doc.manual?.machineLabel ?? '(sem etiqueta)'} · ${doc.client?.name ?? ''}`));
console.log(dim(`${doc.collectors?.length ?? 0} coletores · elevado: ${doc.execution?.elevated ? 'sim' : 'não'} · ${includePending ? 'INCLUINDO regras pendentes' : 'apenas regras habilitadas'}`));
console.log('');
console.log(`Score: ${bandColor(`${value}/100 (${band})`)}   Veredito: ${bold(verdictPt)}`);
if (verdictDrivenBy.length) console.log(dim(`   determinado por: ${verdictDrivenBy.join(', ')}`));
console.log('');

const byState = (s) => findings.filter((f) => f.state === s);

if (byState('NonCompliant').length) {
  console.log(bold('Achados'));
  for (const f of byState('NonCompliant')) {
    const c = { Critical: red, High: red, Medium: yellow, Low: dim, Info: dim }[f.severity];
    const pending = ruleById.get(f.ruleId).enabled ? '' : dim(' [pendente]');
    console.log(`  ${c(f.severity.padEnd(8))} ${f.ruleId}  ${f.title} ${dim(`(-${f.weight})`)}${pending}`);
  }
  console.log('');
}

if (byState('Indeterminate').length) {
  console.log(bold('Não foi possível verificar'));
  for (const f of byState('Indeterminate')) {
    console.log(`  ${dim('·')} ${f.ruleId}  ${f.title}`);
    console.log(`      ${dim(f.indeterminateReason)}`);
  }
  console.log('');
}

console.log(dim(`${byState('Compliant').length} conforme · ${byState('NonCompliant').length} não conforme · ${byState('Indeterminate').length} indeterminado`));

if (evaluationWarnings.length) {
  console.log('');
  console.log(bold('Avisos de avaliação'));
  for (const w of evaluationWarnings) console.log(`  ${yellow('!')} ${w}`);
}
console.log('');
