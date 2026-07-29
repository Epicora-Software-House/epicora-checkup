# ADR-002 — Distribuição do binário

**Estado:** ✅ **Aceita — opção A, repositório público**
**Data de abertura:** 2026-07-29
**Data da decisão:** 2026-07-29
**Referência:** doc técnico §8.2 e §11 ponto 2

## Contexto

O download precisa acontecer na máquina do cliente, pelo navegador, sem Git e sem instalador. O padrão de URL estável do GitHub Releases (`/releases/latest/download/<asset>`) funciona diretamente **apenas em repositório público**. Em repositório privado o download exige Personal Access Token no cabeçalho.

Trilema, sem opção indolor:

| Opção | Vantagem | Custo real |
|---|---|---|
| **A. Repositório público** | Download trivial, uma URL, funciona em qualquer máquina | Código-fonte, regras de risco e textos comerciais visíveis para concorrentes e clientes |
| **B. Repositório privado + PAT** | Código protegido | Token embutido no launcher **é token vazado**. Token digitado pelo técnico é atrito em cada máquina |
| **C. Fonte privado + distribuição separada** | Código protegido e download trivial | Mais uma peça de infraestrutura para manter |

## Recomendação técnica original

A recomendação deste ADR era a **opção C** — repositório privado com o binário publicado em S3/CloudFront sob URL própria da Epicora. A opção A foi registrada como legítima se a direção não considerasse o código um ativo a proteger.

## Decisão

**Opção A — repositório público**, em `github.com/Epicora-Software-House/epicora-checkup`.

- Escolhida por: direção da Epicora
- Data: 2026-07-29
- Contexto: a decisão foi tomada no momento da publicação do repositório, com a exposição abaixo apresentada e reafirmada.

## Consequências aceitas

O que passa a ser público, e é mais do que código-fonte:

| O que fica visível | Por que importa |
|---|---|
| `docs/01-especificacao-funcional.md` §2 | Os quatro movimentos comerciais, a tensão entre otimização gratuita e venda, e a estratégia de conversão do diagnóstico |
| `rules/*.json` — campos `clientText` | Texto de proposta pronto, que vai com pouca ou nenhuma edição para dentro da oferta ao cliente |
| A matriz de regras inteira | O critério de avaliação da Epicora: o que ela considera risco, com que peso, e que veredito deriva disso |
| `rules/startup-exclusions.json` | O acervo de fabricantes e sistemas que a Epicora aprendeu a proteger — conhecimento de campo acumulado |
| Este ADR e o ADR-003 | Deliberação interna sobre proteger o código e sobre orçamento de certificado |

Um concorrente pode ler a matriz e replicar o critério de diagnóstico. Isso é conhecido e foi aceito.

**A publicação é irreversível na prática.** Conteúdo no GitHub é indexado, clonado e espelhado; tornar o repositório privado depois não recupera o que já foi lido.

## O que isso destrava

- Download em `https://github.com/Epicora-Software-House/epicora-checkup/releases/latest/download/EpicoraCheckup.exe`, que resolve sempre para o asset mais recente sem mudar de URL. Requisito: o asset precisa ter **nome fixo entre releases**, sem número de versão no nome.
- Nenhum Personal Access Token no caminho do técnico. Zero atrito por máquina.
- Nenhuma infraestrutura extra para manter.

## O que isso impõe ao código da Fase 3

A verificação de versão passa a usar a API de releases do GitHub, que tem **limite de requisições para chamadas não autenticadas** — a ordem de grandeza precisa ser confirmada na documentação atual do GitHub antes de implementar. Vários técnicos atrás do mesmo IP de cliente podem esbarrar nisso.

Requisito fixo, que já valia e continua valendo: **falha na verificação de versão nunca bloqueia a execução.** Timeout de 3 segundos, erro silencioso, segue em frente. Com o limite de requisição em jogo, isso deixa de ser cortesia e vira necessidade.

## Se a decisão for revista

Voltar para privado é possível e continua útil — impede *novas* leituras e futuras versões da matriz. Mas o que já foi publicado até o momento da troca deve ser considerado público para sempre. A revisão, se vier, precisa vir acompanhada de uma decisão sobre o que fazer com os `clientText` já expostos.
