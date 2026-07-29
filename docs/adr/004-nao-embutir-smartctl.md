# ADR-004 — Não embutir `smartctl` na v1

**Estado:** Aceita
**Data:** 2026-07-29
**Referência:** doc técnico §4.3 e §11 ponto 4

## Contexto

SMART detalhado — horas ligado, contagem de setores realocados, percentual de desgaste de SSD — não tem caminho puro e confiável em WMI. O caminho realista é embutir `smartctl` (smartmontools) como recurso, extrair em pasta temporária e parsear a saída JSON.

## Decisão

**Não embutir `smartctl` na v1.**

## Motivos

1. **Licença.** smartmontools é GPL. Distribuir junto com software proprietário tem implicações que exigiriam análise jurídica antes de qualquer release — custo desproporcional ao ganho nesta etapa.
2. **Perfil de antivírus.** Extrair um segundo executável desconhecido em pasta temporária e executá-lo elevado, para varrer disco em baixo nível, é precisamente o comportamento que dispara EDR. Estamos somando risco à parte mais frágil do projeto ([ADR-003](003-certificado-de-assinatura.md)).
3. **Tamanho.** Contraria a razão de ter escolhido .NET Framework em vez de .NET self-contained: um binário de 1–3 MB que baixa rápido e levanta menos suspeita.
4. **Existe substituto adequado.** O próprio documento 03 registra, em EST-002, que **erro de disco no log de eventos frequentemente antecede falha física e é evidência mais confiável que o SMART básico.**

## O que fica no lugar

Três sinais, nenhum deles exigindo binário externo:

| Sinal | Fonte | Regra |
|---|---|---|
| Predição de falha SMART (booleano) | `MSStorageDriver_FailurePredictStatus` em `root\wmi` | STO-004 |
| Estado de saúde do disco | `MSFT_PhysicalDisk.HealthStatus` | STO-005 |
| Erros de disco no log de eventos | Event Log, canal System | EST-002 |

A coincidência de STO-004 com EST-002 é o sinal mais forte possível de disco morrendo, e é obtida sem nenhuma dependência externa.

## O que se perde, declaradamente

- Horas ligado do disco.
- Contagem de setores realocados.
- Percentual de desgaste de SSD (*wear leveling*).

Nenhuma regra da matriz depende desses campos hoje. O relatório **não deve prometê-los**.

## Reavaliação

Depois de 30 diagnósticos reais, verificar quantas máquinas tiveram disco em falha que os três sinais acima **não** detectaram. Se houver falso negativo relevante, reabrir — com parecer jurídico sobre a GPL antes.
