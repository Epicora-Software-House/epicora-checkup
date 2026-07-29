#!/usr/bin/env node
// Anonimiza um JSON de coleta antes de virar fixture comitada.
//
// OBRIGATÓRIO antes de qualquer commit em tests/fixtures/: o repositório vai para o
// GitHub e os dados são de cliente. LGPD, não preferência.
//
//   node tools/anonymize-fixture.mjs <entrada.json> [saida.json] [--rotulo ADM-04]
//
// O que substitui, de forma DETERMINÍSTICA (mesmo valor original → mesmo pseudônimo,
// para que a deduplicação por UUID e as relações entre campos continuem valendo):
//   hostname · nome do cliente e unidade · nome do técnico · id do diagnóstico
//   serial de produto, BIOS, placa-mãe e disco · UUID · endereços MAC e IP
//   nome de domínio · nomes de conta e SIDs · caminhos com nome de usuário

import { readFileSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';

const args = process.argv.slice(2);
const flagIdx = args.indexOf('--rotulo');
const label = flagIdx >= 0 ? args[flagIdx + 1] : null;
const labelValueIdx = flagIdx >= 0 ? flagIdx + 1 : -1;
const positional = args.filter((a, i) => !a.startsWith('--') && i !== labelValueIdx);
const [input, output] = positional;

if (!input) {
  console.error('uso: node tools/anonymize-fixture.mjs <entrada.json> [saida.json] [--rotulo ADM-04]');
  process.exit(2);
}

const doc = JSON.parse(readFileSync(input, 'utf8'));

/** Pseudônimo estável e curto derivado do valor original. Não é reversível. */
const seen = new Map();
function pseudo(prefix, original, len = 6) {
  if (original === null || original === undefined || original === '') return original;
  const key = `${prefix}:${original}`;
  if (!seen.has(key)) {
    const h = createHash('sha256').update(key).digest('hex').slice(0, len).toUpperCase();
    seen.set(key, `${prefix}-${h}`);
  }
  return seen.get(key);
}

// Resolvedores públicos não são PII, e preservá-los é o que permite conferir
// o campo derivado publicDnsInDomainEnvironment contra o dado bruto na fixture.
const PUBLIC_DNS = new Set([
  '8.8.8.8', '8.8.4.4', '1.1.1.1', '1.0.0.1', '9.9.9.9', '149.112.112.112',
  '208.67.222.222', '208.67.220.220', '76.76.2.0', '94.140.14.14',
]);

function pseudoIp(ip) {
  if (typeof ip !== 'string') return ip;
  if (PUBLIC_DNS.has(ip)) return ip;
  if (ip.includes(':')) return '2001:db8::' + createHash('sha256').update(ip).digest('hex').slice(0, 4);
  const parts = ip.split('.');
  if (parts.length !== 4) return ip;
  const h = createHash('sha256').update(ip).digest();
  return `192.0.2.${(h[0] % 254) + 1}`;   // faixa TEST-NET-1, reservada para documentação
}

function pseudoMac(mac) {
  if (typeof mac !== 'string') return mac;
  const h = createHash('sha256').update(mac).digest();
  return `00:00:5E:${[h[0], h[1], h[2]].map((b) => b.toString(16).padStart(2, '0').toUpperCase()).join(':')}`;
}

const HOST = pseudo('HOST', doc.collectors?.find((c) => c.id === 'machine')?.data?.hostname ?? 'unknown', 4);

/** Percorre o documento e reescreve os campos sensíveis por nome de chave. */
function scrub(node, keyPath = []) {
  if (Array.isArray(node)) return node.map((v, i) => scrub(v, [...keyPath, String(i)]));
  if (node === null || typeof node !== 'object') return node;

  const out = {};
  for (const [k, v] of Object.entries(node)) {
    const path = [...keyPath, k];
    if (v === null) { out[k] = null; continue; }

    switch (k) {
      case 'hostname': out[k] = HOST; break;
      case 'uuid': out[k] = pseudo('UUID', v, 12); break;
      case 'serial': case 'productSerial': out[k] = pseudo('SN', v); break;
      case 'macAddress': out[k] = pseudoMac(v); break;
      case 'defaultGateway': out[k] = pseudoIp(v); break;
      case 'domain': out[k] = typeof v === 'string' ? pseudo('DOM', v, 4).toLowerCase() + '.local' : v; break;
      case 'workgroup': out[k] = typeof v === 'string' ? pseudo('WG', v, 4) : v; break;
      case 'sid': out[k] = typeof v === 'string' && v.startsWith('S-1-5-21')
        ? 'S-1-5-21-0-0-0-' + (v.split('-').pop() ?? '0') : v; break;   // preserva o RID, que é o que importa
      case 'ipAddresses': case 'dnsServers':
        out[k] = Array.isArray(v) ? v.map(pseudoIp) : v; break;
      case 'name':
        // nome de conta em accounts é PII; nome de adaptador, bateria ou dispositivo não é
        out[k] = keyPath.some((p) => ['localAdministrators', 'currentUser', 'localAccounts'].includes(p))
          ? pseudo('USR', v) : v;
        break;
      case 'command': case 'errorDescription':
        out[k] = typeof v === 'string' ? scrubUserPaths(v) : v; break;
      default:
        out[k] = scrub(v, path);
    }
  }
  return out;
}

/** C:\Users\joao.silva\... → C:\Users\<usuario>\... */
function scrubUserPaths(s) {
  return s.replace(/([A-Za-z]:\\Users\\)[^\\"\s]+/gi, '$1<usuario>')
    .replace(/(\\Users\\)[^\\"\s]+/gi, '$1<usuario>');
}

const result = scrub(doc);

// Blocos de identificação: substituídos por inteiro, não por chave
result.client = { name: 'Cliente Anonimizado', unit: doc.client?.unit ? 'Unidade' : null };
result.execution = {
  ...result.execution,
  technician: 'Técnico Anonimizado',
  diagnosticId: pseudo('DIAG', doc.execution?.diagnosticId ?? 'x', 6),
};
result.manual = {
  ...result.manual,
  machineLabel: label ?? pseudo('MAQ', doc.manual?.machineLabel ?? HOST, 4),
  responsible: 'Usuário Anonimizado',
  department: doc.manual?.department ?? 'Setor',
  physicalLocation: doc.manual?.physicalLocation ? 'Local' : null,
  assetTag: doc.manual?.assetTag ? pseudo('PAT', doc.manual.assetTag, 4) : null,
  notes: doc.manual?.notes ? '[observações removidas na anonimização]' : null,
};
// physicalCondition é observação técnica sobre o equipamento, não PII — preservada.

const dest = output ?? input.replace(/(-raw)?\.json$/, '.json').replace(/\.json$/, '.anon.json');
writeFileSync(dest, JSON.stringify(result, null, 2) + '\n');

console.log(`Anonimizado: ${dest}`);
console.log(`${seen.size} valor(es) pseudonimizados de forma determinística.`);
console.log('');
console.log('CONFIRA MANUALMENTE antes de comitar — o anonimizador cobre os campos conhecidos');
console.log('do schema, não texto livre que um coletor futuro venha a incluir:');
console.log(`  grep -iE "$(whoami)|nome-do-cliente" ${dest}`);
