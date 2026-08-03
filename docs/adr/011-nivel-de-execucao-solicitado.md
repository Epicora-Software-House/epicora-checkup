# ADR-011 — Nível de execução solicitado: `highestAvailable`, não `requireAdministrator`

**Estado:** Aceita — **confirmar com a direção técnica**
**Data:** 2026-08-03
**Referência:** doc 02 §3.4; doc 01 §4 e §9 critério de aceite 5

## Contexto

O documento técnico §3.4 diz duas coisas que não podem valer ao mesmo tempo:

> `app.manifest` com `requestedExecutionLevel` como `requireAdministrator`.

E, no parágrafo seguinte:

> Mas o requisito funcional é mais forte que isso: **a ferramenta deve rodar sem elevação também.** Cenário real: técnico sem a senha de administrador local na máquina do cliente.

O critério de aceite 5 do documento funcional repete o segundo:

> Executado **sem** privilégio de administrador, a ferramenta roda, marca as etapas privilegiadas como "Ignorado — sem privilégio" e ainda gera relatório útil.

O conflito é do Windows, não de interpretação. `requestedExecutionLevel` tem três valores, e o que cada um faz com um usuário **sem** direitos administrativos é o que decide:

| Valor | Usuário administrador | Usuário padrão, sem a senha de admin |
|---|---|---|
| `requireAdministrator` | pede confirmação do UAC e eleva | pede **credencial de administrador**. Sem ela, **o processo não inicia** |
| `highestAvailable` | pede confirmação do UAC e eleva | **inicia sem elevação**, sem pedir nada |
| `asInvoker` | inicia sem elevar | inicia sem elevar |

Com `requireAdministrator`, o cenário que o documento chama de real — técnico sem a senha — não produz relatório parcial. Produz uma caixa de diálogo que ele não consegue passar e um executável que não abre. O critério de aceite 5 seria impossível de cumprir.

## Decisão

**`requestedExecutionLevel level="highestAvailable" uiAccess="false"`.**

- Onde há direito administrativo, a ferramenta eleva e coleta TPM, BitLocker e SMART.
- Onde não há, ela abre, coleta tudo o que responde sem privilégio, e marca as três fontes privilegiadas como indeterminadas com motivo.

`asInvoker` foi descartado: um técnico que **é** administrador rodaria sem elevação sem perceber e perderia dado de graça em toda visita.

## Por que não é contornar o documento

O §3.4 declara o requisito funcional como sendo mais forte que a escolha de manifest que ele mesmo sugere, e pede que o valor exato do atributo seja confirmado na documentação. É o que foi feito. A recomendação de `requireAdministrator` é incompatível com o requisito que o próprio parágrafo declara mais forte, então o requisito vence e o manifest muda.

**O que precisa de confirmação da direção técnica:** se a intenção original era de fato exigir elevação sempre, então o critério de aceite 5 e o cenário "técnico sem senha" precisam sair dos documentos. Não se pode manter os três.

## Consequência que não é sobre manifest

Elevação passa a ser **estado a detectar e reportar**, não pré-condição. Em consequência:

- `CollectionContext.IsElevated` é detectado na inicialização e propagado.
- A tela 1 informa em qual dos dois modos está rodando, antes de o técnico iniciar a coleta — descobrir depois que metade das fontes privilegiadas ficou indeterminada é retrabalho de visita.
- O JSON de saída grava `execution.elevated`, que o consolidador usa para não comparar máquina elevada com máquina não elevada como se fossem equivalentes.

A sonda de campo já mostrou que o custo de rodar sem elevação é menor do que o documento supunha: só TPM, BitLocker e SMART exigem privilégio. Nenhum coletor inteiro é descartado.
