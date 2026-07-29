# ADR-005 — Tabela de builds do Windows, com validade explícita

**Estado:** Aceita
**Data:** 2026-07-29
**Referência:** doc técnico §4.4 e §11 ponto 5; matriz OS-005

## Contexto

`Win32_QuickFixEngineering` não lista tudo — atualizações cumulativas modernas frequentemente não aparecem. O caminho confiável para responder "esta máquina está atualizada?" é comparar a build atual (`Win32_OperatingSystem` + UBR do registro) com a build mais recente conhecida.

Isso exige manter uma tabela de builds no repositório, o que é **manutenção recorrente**. O documento registra o dilema: sem a tabela, o achado fica impreciso; com a tabela desatualizada, a regra vira fábrica de falso positivo silencioso — que é o pior dos dois mundos, porque ninguém percebe.

## Decisão

**Manter a tabela, em `rules/windows-builds.json`, com um campo `validUntil` obrigatório.**

O motor de regras verifica `validUntil` antes de avaliar OS-005:

- Tabela **dentro da validade** → OS-005 avalia normalmente.
- Tabela **vencida** → OS-005 resolve `Indeterminate`, com o motivo `"tabela de builds vencida em <data> — não foi possível avaliar se a máquina está atualizada"`.

## Por que isso resolve o dilema

Converte manutenção esquecida em **degradação segura** em vez de falso positivo. O relatório passa a dizer "não foi possível verificar" — que é um resultado honesto e previsto pelo princípio 3 do documento funcional — em vez de afirmar que uma máquina atualizada está desatualizada.

O mesmo mecanismo vale para [ADR-006](006-lista-de-cpus-windows-11.md).

## Formato

```jsonc
{
  "validUntil": "2026-12-31",
  "sourceUrl": "https://learn.microsoft.com/...",
  "lastUpdated": "2026-07-29",
  "builds": [
    { "product": "Windows 11", "displayVersion": "24H2", "build": 26100, "latestUbr": 0000, "eol": "AAAA-MM-DD" }
  ]
}
```

`latestUbr` e as datas de EOL precisam ser levantados na documentação oficial — não preencher de memória.

## Manutenção

- Prazo de validade sugerido: **90 dias** a partir de cada atualização.
- A revisão entra no mesmo ciclo da revisão da matriz de regras (doc 03 §6: a cada dez diagnósticos).
- O validador `tools/validate-rules.mjs` avisa quando faltarem menos de 30 dias para o vencimento.
