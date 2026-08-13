# Matriz de regras

Um arquivo por categoria, espelhando as seções 4.1–4.10 de `docs/03-matriz-riscos-otimizacoes.md`. O comercial revisa `clientText` por categoria e o diff de revisão fica legível.

**Regras nunca são deletadas.** Marcam-se `enabled: false`, para que relatórios antigos permaneçam auditáveis e reprodutíveis.

## `matriz.json` — a versão da matriz

Não contém regras: declara a **data da revisão** da matriz, no formato `AAAA.MM.DD`. É a metade legível de `tool.rulesVersion`, que o relatório grava como `2026.08.12+9f3c1ab2` — data declarada mais impressão digital do conteúdo carregado ([ADR-015](../docs/adr/015-versionamento-da-matriz.md)).

**Bumpar ao alterar regra que muda avaliação.** Esquecer não falsifica o relatório: sai a data velha ao lado de uma impressão digital nova, o que é legível e é verdade. A impressão digital não depende de ninguém lembrar de nada.

Junto de `event-ids.json`, `windows-builds.json`, `win11-cpu-support.json` e `startup-exclusions.json`, este arquivo está na lista de **apoio** — do `RuleRepository` e do `tools/validate-rules.mjs`. Arquivo de apoio novo precisa entrar nas duas listas, senão o carregamento falha por não achar a lista `rules`.

## Formato

```jsonc
{
  "id": "STO-001",
  "version": 1,
  "enabled": false,
  "enabledBlockedBy": ["clientText", "sourceValidation"],
  "sourceConfidence": "M",
  "validationNote": "o que precisa ser confirmado em campo antes de habilitar",
  "category": "Armazenamento",
  "severity": "Critical",
  "weight": 25,
  "requires": ["collectors.storage.data.systemDisk.mediaType"],
  "indeterminateWhen": { "path": "...", "operator": "equals", "value": "Unknown" },
  "condition": { "path": "...", "operator": "equals", "value": "HDD" },
  "title": "Disco de sistema é HDD",
  "clientText": "...",
  "recommendedAction": "...",
  "evidenceFields": ["collectors.storage.data.systemDisk.model"],
  "linkedOptimizations": [],
  "verdictInfluence": "Replace"
}
```

## Ordem de avaliação

1. **`requires`** — se qualquer caminho listado resolver `null`, ou se o coletor de origem não estiver `Completed`, a regra resolve **`Indeterminate`**. Nunca `NonCompliant`.
2. **`indeterminateWhen`** — se a condição for verdadeira, resolve `Indeterminate` com o motivo. É o que impede que um valor `"Unknown"` de enum passe silenciosamente por `notEquals` e vire `Compliant`.
3. **`condition`** — verdadeira → `NonCompliant`. Falsa → `Compliant`.

`Indeterminate` não pontua no score e aparece no bloco "não foi possível verificar" do relatório, com o motivo.

## Operadores

`equals` · `notEquals` · `lessThan` · `greaterThan` · `contains` · `notContains` · `isTrue` · `isFalse` · `isNull` · `isNotNull` · `inList` · `notInList` · `isEmpty` · `isNotEmpty`

Composição: `allOf` · `anyOf` · `not`.

O conjunto é deliberadamente pequeno. **Regra que não cabe nele precisa de um campo derivado calculado no coletor, que é mais testável** — é assim que `memory.freeSlots`, `network.linkDowngraded`, `antivirus.overallConfidence` e `software.outdatedBrowsers` existem.

Três extensões sobre o formato do documento 03, todas justificadas:

| Extensão | Por quê |
|---|---|
| `indeterminateWhen` | Sem ela, `mediaType: "Unknown"` avaliado por `notEquals "HDD"` resolveria `Compliant` — um falso negativo silencioso |
| `isEmpty` / `isNotEmpty` | Arrays de classificação (`backupAgents`, `remoteAccessTools`) precisam de teste de vazio. A alternativa seria um campo de contagem para cada, o que é pior |
| `enabledBlockedBy` / `validationNote` | Torna auditável **por que** uma regra está desligada e **o que** precisa acontecer para ligá-la |

## Por que quase tudo nasce desligado

Doc 03 §7: *"Regra habilitada sobre fonte não validada é fábrica de falso positivo."*

Uma regra só nasce `enabled: true` quando cumpre as duas condições:

1. **Fonte de confiança A** no documento técnico §4 — classe WMI clássica e estável.
2. **`clientText` redigido e aprovado pelo comercial** (doc 03 §1.3).

Hoje isso são **5 regras**. As outras 56 esperam a Fase 1: cada fonte validada em campo destrava as regras que dependem dela, uma a uma.

Para ver o que as regras desligadas *produziriam* — útil para conferir a matriz antes do campo:

```sh
node tools/evaluate-rules.mjs tests/fixtures/sintetica-vermelha.json --incluir-pendentes
```

## Arquivos de apoio

| Arquivo | Conteúdo | Estado |
|---|---|---|
| `startup-exclusions.json` | Fabricantes e processos que **não podem** ser desativados na Fase 5 | Semeado, cresce com o campo |
| `event-ids.json` | IDs de evento do Windows por categoria | **IDs levantados** com fonte oficial (2026-08-03); `validUntil` nulo até validação de campo |
| `windows-builds.json` | Build mais recente conhecida, com `validUntil` | **Vazio** — ver ADR-005 |
| `win11-cpu-support.json` | CPUs suportadas por fabricante, com `validUntil` | **Vazio** — ver ADR-006 |

Arquivo de apoio vazio ou vencido não gera falso positivo: a regra que depende dele resolve `Indeterminate`.
