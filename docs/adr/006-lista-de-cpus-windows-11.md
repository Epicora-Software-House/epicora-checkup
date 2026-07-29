# ADR-006 — Lista de CPUs suportadas para Windows 11

**Estado:** Aceita
**Data:** 2026-07-29
**Referência:** doc técnico §4.5 e §11 ponto 6; matriz CPU-002

## Contexto

O veredito de compatibilidade com Windows 11 é a categoria de maior valor comercial da ferramenta — sozinha justifica um relatório e sustenta a frase "18 das 50 máquinas não migram". Ela depende de TPM, Secure Boot, firmware e **modelo de processador**.

A Microsoft publica listas de CPUs suportadas por fabricante. Sem embuti-las, o veredito fica parcial.

## Decisão

**Embutir a lista em `rules/win11-cpu-support.json`, com o mesmo mecanismo `validUntil` do [ADR-005](005-tabela-de-builds-do-windows.md).**

Enquanto a lista não estiver preenchida e validada, **CPU-002 permanece `enabled: false`** e o relatório declara explicitamente, no bloco "não foi possível verificar":

> A compatibilidade do processador com o Windows 11 não foi avaliada por esta ferramenta.

## Por que declarar em vez de arriscar

Afirmar incompatibilidade de CPU sem base é o tipo de erro que custa um cliente: o técnico recomenda substituir uma máquina que na verdade migra. E o erro inverso é igualmente caro. O documento 03 §4.3 é explícito — se a lista não for embutida, a regra nasce desabilitada.

Um veredito parcial e declarado é vendável. Um veredito errado, não.

## Formato

```jsonc
{
  "validUntil": "2026-12-31",
  "lastUpdated": "2026-07-29",
  "sources": {
    "intel": "https://learn.microsoft.com/...",
    "amd":   "https://learn.microsoft.com/...",
    "qualcomm": "https://learn.microsoft.com/..."
  },
  "supported": {
    "intel": ["..."],
    "amd": ["..."],
    "qualcomm": ["..."]
  }
}
```

## Ponto de implementação — a parte difícil não é a lista

É o **casamento** entre `Win32_Processor.Name` (que vem com formatação inconsistente entre fabricantes, com `(R)`, `(TM)`, espaçamento irregular e sufixos) e a entrada da lista oficial.

Requisito: normalizar antes de comparar, e **quando o casamento não for inequívoco, resolver `Indeterminate`** — nunca "não suportado". Vale o mesmo princípio da interpretação do estado de antivírus: na dúvida, "não sei".

O casamento precisa ser testado contra os nomes de CPU reais colhidos na Fase 1, não contra nomes inventados.
