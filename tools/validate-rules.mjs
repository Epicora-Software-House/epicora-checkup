#!/usr/bin/env node
// Valida rules/*.json contra o schema de coleta e contra as regras de governança
// do doc 03 §1 e §6. Roda no Mac. Entra no CI na Fase 3.
//
// O que verifica:
//   1. IDs únicos e formato de regra completo
//   2. Todo caminho citado existe no schema de coleta — pega typo antes do campo
//   3. Regra enabled tem clientText e recommendedAction (doc 03 §1.3)
//   4. Regra enabled não depende de fonte de confiança M ou B (doc 03 §7)
//   5. enabled e enabledBlockedBy são consistentes entre si
//   6. linkedOptimizations referenciam ações do catálogo (doc 03 §5.1)
//   7. Arquivos de apoio com validUntir próximo do vencimento ou vencido

import { readFileSync, readdirSync } from 'node:fs';
import { join, dirname, basename } from 'node:path';
import { fileURLToPath } from 'node:url';

/** JSON vindo de máquina Windows pode ter BOM UTF-8, e JSON.parse rejeita BOM. */
const readJson = (p) => JSON.parse(readFileSync(p, 'utf8').replace(/^\uFEFF/, ''));


const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const RULES_DIR = join(ROOT, 'rules');
const SCHEMA_PATH = join(ROOT, 'schema', 'checkup-1.0.schema.json');

const red = (s) => `\x1b[31m${s}\x1b[0m`;
const yellow = (s) => `\x1b[33m${s}\x1b[0m`;
const green = (s) => `\x1b[32m${s}\x1b[0m`;
const dim = (s) => `\x1b[2m${s}\x1b[0m`;

const OPERATORS = new Set([
  'equals', 'notEquals', 'lessThan', 'greaterThan', 'contains', 'notContains',
  'isTrue', 'isFalse', 'isNull', 'isNotNull', 'inList', 'notInList', 'isEmpty', 'isNotEmpty',
]);
const VALUELESS = new Set(['isTrue', 'isFalse', 'isNull', 'isNotNull', 'isEmpty', 'isNotEmpty']);
const SEVERITIES = new Set(['Critical', 'High', 'Medium', 'Low', 'Info']);
const VERDICTS = new Set([null, 'Upgrade', 'Replace']);
const CONFIDENCES = new Set(['A', 'M', 'B']);
const BLOCKERS = new Set(['clientText', 'sourceValidation']);

// Catálogo de otimizações do doc 03 §5.1. Fase 5, mas as regras já apontam para ele.
const OPTIMIZATIONS = new Set([
  'OPT-TEMP', 'OPT-WU', 'OPT-OLD', 'OPT-BIN', 'OPT-THUMB',
  'OPT-DUMP', 'OPT-BROWSER', 'OPT-TRIM', 'OPT-DEFRAG', 'OPT-STARTUP',
]);

const SUPPORT_FILES = new Set([
  'startup-exclusions.json', 'event-ids.json', 'windows-builds.json', 'win11-cpu-support.json',
]);

const schema = readJson(SCHEMA_PATH);
const errors = [];
const warnings = [];

// ---------------------------------------------------------------- resolução de caminho

const deref = (node) => {
  let n = node;
  while (n && n.$ref) {
    const parts = n.$ref.replace(/^#\//, '').split('/');
    n = parts.reduce((acc, p) => acc?.[p], schema);
  }
  return n;
};

/** Resolve um caminho pontilhado contra o schema. Devolve null se o caminho não existir. */
function resolvePath(path) {
  const segs = path.split('.');
  let node;

  if (segs[0] === 'collectors') {
    const collectorIds = schema.$defs.collectorResult.properties.id.$ref
      ? deref(schema.$defs.collectorResult.properties.id).enum
      : schema.$defs.collectorId.enum;
    const id = segs[1];
    if (!collectorIds.includes(id)) return { ok: false, why: `coletor "${id}" não existe` };
    if (segs[2] !== 'data') return { ok: false, why: `esperado "data" após collectors.${id}` };
    node = deref(schema.$defs[`data_${id}`]);
    if (!node) return { ok: false, why: `não há shape definido para o coletor "${id}"` };
    segs.splice(0, 3);
  } else {
    node = schema;
  }

  for (const seg of segs) {
    node = deref(node);
    const props = node?.properties;
    if (!props || !(seg in props)) {
      return { ok: false, why: `segmento "${seg}" não existe no schema` };
    }
    node = props[seg];
  }
  return { ok: true, node: deref(node) };
}

function checkPath(path, where, ruleId) {
  const r = resolvePath(path);
  if (!r.ok) errors.push(`${ruleId} · ${where}: caminho inválido "${path}" — ${r.why}`);
  return r;
}

// ---------------------------------------------------------------- validação de condição

function walkCondition(cond, where, rule) {
  if (cond === null || cond === undefined) return;
  if (typeof cond !== 'object') {
    errors.push(`${rule.id} · ${where}: condição precisa ser objeto`);
    return;
  }
  for (const key of ['allOf', 'anyOf']) {
    if (key in cond) {
      if (!Array.isArray(cond[key]) || cond[key].length < 2) {
        errors.push(`${rule.id} · ${where}: "${key}" precisa de pelo menos 2 subcondições`);
        return;
      }
      cond[key].forEach((c, i) => walkCondition(c, `${where}.${key}[${i}]`, rule));
      return;
    }
  }
  if ('not' in cond) return walkCondition(cond.not, `${where}.not`, rule);

  if (!('path' in cond) || !('operator' in cond)) {
    errors.push(`${rule.id} · ${where}: folha precisa de "path" e "operator"`);
    return;
  }
  if (!OPERATORS.has(cond.operator)) {
    errors.push(`${rule.id} · ${where}: operador desconhecido "${cond.operator}"`);
  }
  const needsValue = !VALUELESS.has(cond.operator);
  if (needsValue && !('value' in cond)) {
    errors.push(`${rule.id} · ${where}: operador "${cond.operator}" exige "value"`);
  }
  if (!needsValue && 'value' in cond) {
    errors.push(`${rule.id} · ${where}: operador "${cond.operator}" não aceita "value"`);
  }
  if (['inList', 'notInList'].includes(cond.operator) && !Array.isArray(cond.value)) {
    errors.push(`${rule.id} · ${where}: "${cond.operator}" exige que "value" seja array`);
  }
  checkPath(cond.path, where, rule.id);
}

// ---------------------------------------------------------------- validação de regra

const seenIds = new Map();
let ruleCount = 0;
let enabledCount = 0;

function validateRule(rule, file) {
  ruleCount++;
  const req = ['id', 'version', 'enabled', 'enabledBlockedBy', 'sourceConfidence', 'category',
    'severity', 'weight', 'requires', 'condition', 'title', 'clientText',
    'recommendedAction', 'evidenceFields', 'linkedOptimizations', 'verdictInfluence'];
  for (const f of req) {
    if (!(f in rule)) errors.push(`${rule.id ?? '(sem id)'} em ${file}: falta o campo "${f}"`);
  }
  if (!rule.id) return;

  if (seenIds.has(rule.id)) {
    errors.push(`${rule.id}: ID duplicado — já definido em ${seenIds.get(rule.id)}`);
  }
  seenIds.set(rule.id, file);

  if (!/^[A-Z0-9]{2,4}-\d{3}$/.test(rule.id)) {
    errors.push(`${rule.id}: formato de ID fora do padrão CAT-000`);
  }
  if (!SEVERITIES.has(rule.severity)) errors.push(`${rule.id}: severidade inválida "${rule.severity}"`);
  if (!CONFIDENCES.has(rule.sourceConfidence)) errors.push(`${rule.id}: sourceConfidence inválida "${rule.sourceConfidence}"`);
  if (!VERDICTS.has(rule.verdictInfluence)) errors.push(`${rule.id}: verdictInfluence inválido "${rule.verdictInfluence}"`);
  if (!Number.isInteger(rule.weight) || rule.weight < 0) errors.push(`${rule.id}: weight precisa ser inteiro >= 0`);
  if (rule.severity === 'Info' && rule.weight !== 0) {
    errors.push(`${rule.id}: severidade Info exige weight 0 (não pontua no score)`);
  }

  for (const b of rule.enabledBlockedBy ?? []) {
    if (!BLOCKERS.has(b)) errors.push(`${rule.id}: enabledBlockedBy desconhecido "${b}"`);
  }

  // Governança do doc 03 §1.3 e §7 — o coração deste validador.
  if (rule.enabled) {
    enabledCount++;
    if ((rule.enabledBlockedBy ?? []).length) {
      errors.push(`${rule.id}: enabled=true mas enabledBlockedBy não está vazio`);
    }
    if (!rule.clientText) {
      errors.push(`${rule.id}: regra habilitada sem clientText — doc 03 §1.3 proíbe entrar em release`);
    }
    if (!rule.recommendedAction) {
      errors.push(`${rule.id}: regra habilitada sem recommendedAction`);
    }
    if (rule.sourceConfidence !== 'A') {
      errors.push(`${rule.id}: regra habilitada sobre fonte de confiança ${rule.sourceConfidence} — doc 03 §7: fábrica de falso positivo`);
    }
  } else {
    if (!(rule.enabledBlockedBy ?? []).length) {
      errors.push(`${rule.id}: enabled=false sem enabledBlockedBy — registre o motivo`);
    }
    if (!rule.clientText && !(rule.enabledBlockedBy ?? []).includes('clientText')) {
      errors.push(`${rule.id}: sem clientText mas "clientText" não consta em enabledBlockedBy`);
    }
    if (rule.sourceConfidence !== 'A' && !(rule.enabledBlockedBy ?? []).includes('sourceValidation')) {
      errors.push(`${rule.id}: fonte ${rule.sourceConfidence} mas "sourceValidation" não consta em enabledBlockedBy`);
    }
    if ((rule.enabledBlockedBy ?? []).includes('sourceValidation') && !rule.validationNote) {
      errors.push(`${rule.id}: bloqueada por sourceValidation sem validationNote — o que precisa ser validado?`);
    }
  }

  for (const p of rule.requires ?? []) checkPath(p, 'requires', rule.id);
  for (const p of rule.evidenceFields ?? []) checkPath(p, 'evidenceFields', rule.id);
  walkCondition(rule.condition, 'condition', rule);
  if (rule.indeterminateWhen) walkCondition(rule.indeterminateWhen, 'indeterminateWhen', rule);

  for (const opt of rule.linkedOptimizations ?? []) {
    if (!OPTIMIZATIONS.has(opt)) errors.push(`${rule.id}: otimização desconhecida "${opt}"`);
  }
}

// ---------------------------------------------------------------- arquivos de apoio

// Quantas entradas de dado o arquivo já tem. Serve para distinguir "vazio" de
// "preenchido mas ainda não liberado", que são estados diferentes e o aviso não
// deve confundir: o segundo é progresso real esperando validação de campo.
function countSupportEntries(doc) {
  if (Array.isArray(doc.builds)) return doc.builds.length;
  if (doc.supported) return Object.values(doc.supported).reduce((n, v) => n + (Array.isArray(v) ? v.length : 0), 0);
  if (doc.categories) {
    return Object.values(doc.categories)
      .reduce((n, c) => n + (Array.isArray(c?.events) ? c.events.length : 0), 0);
  }
  return null;   // arquivo sem forma conhecida de dado — não opinar
}

// Invariante do próprio repositório, em rules/event-ids.json: "Cada ID entra aqui
// com verifiedSourceUrl preenchido, não antes." Regra escrita que o validador não
// cobre é regra que alguém quebra de boa-fé.
function validateVerifiedSources(name, doc) {
  if (!doc.categories) return;
  for (const [cat, body] of Object.entries(doc.categories)) {
    for (const ev of body?.events ?? []) {
      const onde = `${name} ${cat} id ${ev.eventId ?? '?'}`;
      if (!ev.verifiedSourceUrl) errors.push(`${onde}: sem verifiedSourceUrl — ID não pode entrar sem fonte oficial`);
      if (!ev.verifiedAt) errors.push(`${onde}: sem verifiedAt`);
      if (!ev.provider) errors.push(`${onde}: sem provider — a consulta precisa filtrar por provedor, ID não é único entre provedores`);
    }
  }
}

function validateSupportFile(name, doc) {
  validateVerifiedSources(name, doc);
  if (!('validUntil' in doc)) return;
  if (doc.validUntil === null) {
    const n = countSupportEntries(doc);
    warnings.push(n
      ? `${name}: ${n} entrada(s) preenchida(s), mas validUntil nulo — não liberado para avaliação. As regras que dependem dele resolvem Indeterminate.`
      : `${name}: validUntil nulo — arquivo ainda não preenchido. As regras que dependem dele resolvem Indeterminate.`);
    return;
  }
  const until = new Date(`${doc.validUntil}T23:59:59Z`);
  if (Number.isNaN(until.getTime())) {
    errors.push(`${name}: validUntil "${doc.validUntil}" não é uma data AAAA-MM-DD válida`);
    return;
  }
  const days = Math.floor((until - new Date()) / 86400000);
  if (days < 0) errors.push(`${name}: VENCIDO há ${-days} dias (validUntil ${doc.validUntil})`);
  else if (days < 30) warnings.push(`${name}: vence em ${days} dias (${doc.validUntil}) — atualizar`);
}

// ---------------------------------------------------------------- execução

for (const file of readdirSync(RULES_DIR).filter((f) => f.endsWith('.json')).sort()) {
  let doc;
  try {
    doc = readJson(join(RULES_DIR, file));
  } catch (err) {
    errors.push(`${file}: JSON inválido — ${err.message}`);
    continue;
  }
  if (SUPPORT_FILES.has(file)) {
    validateSupportFile(file, doc);
    continue;
  }
  if (!Array.isArray(doc.rules)) {
    errors.push(`${file}: falta o array "rules"`);
    continue;
  }
  for (const rule of doc.rules) validateRule(rule, file);
}

// ---------------------------------------------------------------- relatório

for (const w of warnings) console.log(`${yellow('!')} ${w}`);
for (const e of errors) console.log(`${red('✗')} ${e}`);

console.log('');
console.log(dim(`${ruleCount} regras · ${enabledCount} habilitadas · ${ruleCount - enabledCount} aguardando clientText ou validação de fonte`));

if (errors.length) {
  console.log(red(`${errors.length} erro(s).`));
  process.exit(1);
}
console.log(green(`Matriz consistente.${warnings.length ? ` ${warnings.length} aviso(s).` : ''}`));
