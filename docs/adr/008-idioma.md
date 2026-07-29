# ADR-008 — Idioma: pt-BR apenas na v1

**Estado:** Aceita
**Data:** 2026-07-29
**Referência:** doc técnico §11 ponto 8

## Decisão

**Interface e relatório em português do Brasil apenas.** Sem seletor de idioma, sem inglês na v1.

O mercado atendido é regional. Suporte a inglês seria trabalho sem demanda identificada.

## O que se faz agora para não travar i18n depois

Duas medidas, ambas de custo baixo:

1. **Nenhuma string de interface hardcoded no meio do código.** Textos de UI ficam em um único ponto por projeto (`Strings.cs` ou `.resx`). Isso vale por si, independentemente de i18n — é o que permite o comercial revisar texto sem caçar literais.
2. **`clientText` e `recommendedAction` já vivem em `rules/*.json`**, fora do binário. Traduzir a matriz um dia é traduzir dados, não recompilar.

## O que **não** se faz agora

Não criar infraestrutura de localização, arquivos de recurso por cultura, nem abstração de formatação por `CultureInfo` além do que o .NET já faz sozinho. Seria complexidade paga sem uso.

## Ponto de atenção que não é sobre idioma, é sobre localização

Independentemente desta decisão, **nunca comparar nome de grupo ou de conta do Windows por string** — "Administradores" em português, "Administrators" em inglês. Usar sempre o SID conhecido. Doc técnico §4.6 registra isso, e é a armadilha de localização que realmente importa neste projeto: a máquina do cliente pode estar em qualquer idioma, mesmo com a nossa ferramenta em português.
