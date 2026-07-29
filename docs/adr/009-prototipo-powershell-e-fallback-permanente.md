# ADR-009 — O protótipo PowerShell é fallback permanente, não descartável

**Estado:** Aceita
**Data:** 2026-07-29
**Referência:** doc técnico §1 (Fase 1), §8.4 mitigação 4, §11 ponto 9

## Contexto

O documento técnico descreve o `.ps1` da Fase 1 como "descartável — não é base de código, é instrumento de descoberta". Mas a seção 8.4, sobre bloqueio por antivírus, chega a outra conclusão: se o executável for bloqueado e não houver como liberar, o técnico precisa de um caminho alternativo, e esse caminho é o script PowerShell. O próprio documento aponta a contradição e pede que ela seja resolvida na Fase 0.

## Decisão

**O protótipo PowerShell é componente mantido do produto, não artefato descartável.**

Consequências diretas:

- Vive em `tools/prototype/`, versionado e mantido.
- **Emite JSON no mesmo schema** que o executável C#. O consolidador não distingue a origem.
- Entra na revisão a cada mudança de schema. Schema alterado sem atualizar o `.ps1` é build quebrado, não dívida.
- Tem seu próprio procedimento de campo documentado.

## Por que isso é a decisão certa e não conservadorismo

O cenário de bloqueio por EDR **não é hipótese** — é o perfil comportamental exato que EDR moderno bloqueia, e vai acontecer em clientes que têm EDR corporativo, que são justamente os clientes maiores. Sem fallback, o técnico chega na visita e não tem o que fazer.

Além disso, o script tem duas propriedades que o executável nunca terá:

1. **É auditável na hora.** O TI do cliente que se recusa a liberar um binário desconhecido pode abrir o `.ps1` e ler o que ele faz. Isso muda a conversa.
2. **Não tem Mark-of-the-Web relevante nem exige assinatura de binário** — o atrito é de outra natureza (ExecutionPolicy), e mais fácil de contornar com o TI presente.

## Custo aceito

Duas implementações de coleta para manter sincronizadas. Mitigações:

- Os módulos `.psm1` seguem o mesmo contrato do `ICollector` do C# (`Id`, `DisplayName`, `RequiresElevation`, `Collect`) — a Fase 2 é porte, não reescrita.
- **As fixtures são o contrato compartilhado.** Ambas as implementações são verificadas contra o mesmo JSON Schema e o mesmo conjunto de fixtures com saída esperada.
- Quando divergirem, o schema decide.

## Restrição técnica que decorre disso

Alvo é **PowerShell 5.1**, presente em toda instalação de Windows desde o 10 sem instalar nada. Não PowerShell 7 — exigir instalação anula a razão de existir do fallback.

Implica, na prática: `ConvertTo-Json -Depth 10` explícito (o padrão é 2 e trunca em silêncio), sem operador ternário, sem `??`, e atenção a array de item único que colapsa em escalar na serialização.
