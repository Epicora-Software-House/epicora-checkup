# ADR-002 — Distribuição do binário

**Estado:** ⏸ **Pendente — decisão da direção**
**Data de abertura:** 2026-07-29
**Prazo:** antes do início da Fase 3
**Bloqueia:** Fase 3 (distribuição, verificação de versão, CI de release)
**Não bloqueia:** Fases 0, 1 e 2
**Referência:** doc técnico §8.2 e §11 ponto 2

## Contexto

O download precisa acontecer na máquina do cliente, pelo navegador, sem Git e sem instalador. O padrão de URL estável do GitHub Releases (`/releases/latest/download/<asset>`) funciona diretamente **apenas em repositório público**. Em repositório privado o download exige Personal Access Token no cabeçalho.

Trilema, sem opção indolor:

| Opção | Vantagem | Custo real |
|---|---|---|
| **A. Repositório público** | Download trivial, uma URL, funciona em qualquer máquina | Código-fonte, regras de risco e textos comerciais visíveis para concorrentes e clientes |
| **B. Repositório privado + PAT** | Código protegido | Token embutido no launcher **é token vazado**. Token digitado pelo técnico é atrito em cada máquina |
| **C. Fonte privado + distribuição separada** | Código protegido e download trivial | Mais uma peça de infraestrutura para manter |

## Recomendação técnica

**Opção C.** Repositório de código privado no GitHub; o CI publica o binário em bucket S3 com CloudFront (a Epicora já opera AWS), atrás de uma URL curta própria. Custo mensal desprezível para um arquivo de poucos MB, e a URL fica sob controle da Epicora — o que permite revogar acesso e trocar de host sem alterar o procedimento do técnico.

A opção A é legítima se a direção não considerar o código um ativo a proteger. **É decisão de negócio, não técnica.**

## O que muda no código conforme a escolha

- **Opção C:** verificação de versão lê um `latest.json` estático ao lado do binário. Simples, sem limite de requisição.
- **Opção A:** verificação de versão usa a API de releases do GitHub, que tem limite para chamadas não autenticadas (a ordem de grandeza precisa ser confirmada na documentação atual). Vários técnicos atrás do mesmo IP de cliente podem esbarrar nisso.

Em qualquer cenário, requisito fixo: **falha na verificação de versão nunca bloqueia a execução.** Timeout de 3 segundos, erro silencioso, segue em frente.

## Decisão registrada

_A preencher quando a direção decidir._

- Escolha:
- Quem decidiu:
- Data:
