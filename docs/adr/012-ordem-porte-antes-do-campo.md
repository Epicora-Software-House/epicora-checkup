# ADR-012 — Portar os coletores antes de completar a validação de campo

**Estado:** Aceita
**Data:** 2026-08-11
**Decisão de:** Gabriel Oss
**Referência:** doc 01 §11 (fases) e §9 (critérios de aceite); [ADR-009](009-prototipo-powershell-e-fallback-permanente.md)

## Contexto

O documento funcional §11 declara que a **Fase 2 depende da Fase 1**, e a Fase 1 é "protótipo em PowerShell, testado em 5–10 máquinas reais".

Hoje são **duas máquinas**, ambas notebooks:

| Máquina | Data | O que rendeu |
|---|---|---|
| DELL-G15 | 2026-07-29 | Reescrita de 189 linhas do coletor; falso negativo de `SEC-007` corrigido |
| JULIA-LAPTOP (Acer) | 2026-08-11 | Três bugs: provedor de NTFS errado, contaminação de evento medida, SMART perdido em máquina com dois discos |

Faltam os perfis que o próprio §9 lista: desktop, máquina com disco de sistema HDD, máquina em domínio, máquina com EDR de terceiro, máquina sem TPM.

A recomendação técnica registrada era **completar o campo antes de portar**, por um argumento de custo: o [ADR-009](009-prototipo-powershell-e-fallback-permanente.md) fixou que o `.ps1` é fallback permanente e entra na revisão a cada mudança. A partir do porte, **toda correção de coletor passa a custar duas implementações**, não uma.

## Decisão

**Portar os coletores para C# agora. A validação de campo é retomada quando a ferramenta estiver completa, e passa a ser feita com o executável, não com o protótipo.**

Testes em máquinas adicionais e a aprovação de `clientText` pelo comercial ficam em espera declarada.

## Motivo

Uma ferramenta que não coleta não é testável por ninguém além de quem a escreve. Levar o executável ao estado de funcionar ponta a ponta permite distribuir para mais gente testar de uma vez, em vez de validar fonte por fonte com um instrumento que não é o produto final.

## Consequências, e elas são reais

1. **Toda correção de coletor custa dobrado a partir daqui.** É o custo aceito, não um efeito colateral esquecido. O `.ps1` continua vivo e em sincronia — o ADR-009 não é revogado por este.

2. **É provável que apareçam mais bugs de coleta.** A máquina nº 2 rendeu três, incluindo um que só aparece com dois discos — configuração corriqueira que a máquina nº 1 não tinha. O porte carrega para o C# tudo o que ainda não foi medido.

3. **A matriz continua com 5 regras habilitadas de 61.** A saída da ferramenta segue magra: 49 aguardam `clientText` do comercial e 7 aguardam validação de fonte. Isso não muda com o porte.

4. **Campos que hoje resolvem `Indeterminate` continuam resolvendo.** Bateria sem desgaste, `win11.eligible` nulo, `events.evaluated` falso, `NET-001` sem base. Nenhum deles é destravado por escrever C#.

## O que este ADR NÃO faz

**Não libera uso comercial.** Os critérios de aceite 7 e 8 do documento funcional continuam valendo integralmente:

> 7. Testado em, no mínimo: um desktop antigo com HDD, um notebook com SSD, uma máquina em domínio, uma máquina com EDR de terceiro, uma máquina sem TPM.
> 8. Zero falso positivo nas dez primeiras máquinas reais, ou regra corrigida antes de qualquer uso comercial.

Isto é uma mudança de **ordem de trabalho**, não de exigência. O diagnóstico só vai a cliente depois das dez máquinas, com ou sem executável pronto.

## Revisão

Reabrir se o porte começar a produzir retrabalho de campo em volume — isto é, se cada máquina nova exigir corrigir o mesmo defeito nos dois lugares mais de duas ou três vezes. Nesse ponto o custo do porte antecipado terá superado o ganho, e vale parar e ir a campo.
