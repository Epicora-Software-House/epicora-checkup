# ADR-014 — Verificação de versão pela própria ferramenta

**Estado:** ✅ Aceita
**Data:** 2026-08-13
**Decisão de:** Gabriel Oss
**Referência:** doc 01 §4 (fluxo do técnico, item 4) e §5 (princípios da distribuição); doc 02 §8.3 (verificação de versão) e §9 (log); [ADR-002](002-distribuicao-do-binario.md), [ADR-013](013-executavel-unico.md)

## Contexto

O doc 01 §4 põe a verificação de versão no passo 4 do fluxo do técnico: a ferramenta confere a própria versão contra o release mais recente e avisa se estiver desatualizada. O princípio da distribuição é "sempre a versão mais recente — o técnico nunca carrega uma cópia antiga".

O [ADR-013](013-executavel-unico.md) deixou isso pendente por um motivo que não é técnico:

> Ela exige que a ferramenta faça uma requisição externa a partir da máquina do cliente, e o README afirma, em primeira linha, que a ferramenta *não abre porta de rede e não faz telemetria*. **É decisão de produto, e fica pendente.**

É essa pendência que este ADR fecha.

## Decisão

**A ferramenta consulta a API de releases do GitHub na abertura e avisa, sem bloquear, quando há versão mais nova.**

| O que | Como |
|---|---|
| Endpoint | `https://api.github.com/repos/Epicora-Software-House/epicora-checkup/releases/latest`, sem autenticação |
| Quando | Na tela 1, fora da thread da interface, uma vez por execução |
| Tempo limite | 3 segundos, dos dois lados (estabelecer resposta e ler corpo) |
| Falha | Registra uma linha no log e segue. Nunca bloqueia, nunca mostra erro |
| Aviso | Caixa na tela 1 com as duas versões e um link de download. **Não desabilita o botão de iniciar** |
| Registro | O resultado vai para o log em toda execução, como o doc 02 §9 exige |

## A promessa ao cliente, e por que ela continua verdadeira

A frase do README precisa mudar, e muda: passa a dizer que existe **uma** consulta de versão. O que continua verdade, literalmente:

- **Não abre porta de rede.** Requisição de saída não é porta aberta. A ferramenta não escuta em nada, não aceita conexão, não é alcançável.
- **Não faz telemetria.** A requisição é um `GET` sem corpo. Nenhum dado da máquina, do cliente ou do diagnóstico sai dela — nem hostname, nem serial, nem número de diagnóstico, nem contagem de achados.

E o que é honesto declarar, porque acontece:

- **O GitHub registra que um IP consultou o endpoint**, com data, hora e o `User-Agent` `EpicoraCheckup` — que é obrigatório na API. Ou seja: o GitHub fica sabendo que a ferramenta da Epicora rodou naquela rede, naquele momento. Não fica sabendo em qual máquina, para qual cliente, nem o que ela achou.

Se o responsável de TI do cliente perguntar, essa é a resposta completa. Nada aqui depende de o cliente confiar na palavra: o repositório é público (ADR-002) e o código da verificação é uma classe de trinta linhas.

## Alternativas consideradas

**Arquivo estático em S3/CloudFront** (a opção C do doc 02 §8.2, que o ADR-013 sugeriu como conciliação). Tecnicamente melhor: sem limite de requisição e sob domínio da Epicora. Perde porque o [ADR-002](002-distribuicao-do-binario.md) já decidiu a opção A — repositório público, sem infraestrutura extra — e montar um bucket só para o arquivo de versão reintroduz a peça de infraestrutura que aquela decisão eliminou. Se o ADR-002 for revisto, este ponto volta junto.

**Não verificar nada, e confiar no procedimento.** É o que valia até aqui. Perde porque o modo de falhar é silencioso: um técnico com uma cópia de duas semanas atrás produz relatório com o critério antigo, e nada na tela indica isso. Aviso na abertura é a única barreira antes de o relatório existir.

**Bloquear a execução quando desatualizada.** Recusada. Pode não haver como baixar naquele momento — rede do cliente restrita, técnico no meio de uma visita — e um diagnóstico com a matriz de duas semanas atrás vale mais que nenhum diagnóstico. O aviso informa; quem decide é o técnico.

## O limite de requisição, confirmado

O ADR-002 exigiu confirmar a ordem de grandeza na documentação atual do GitHub antes de implementar. Confirmado em **2026-08-13**, em `docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api`:

> "The primary rate limit for unauthenticated requests is 60 requests per hour." […] "Unauthenticated requests are associated with the originating IP address, not with the user or application that made the request."

**60 por hora, por IP de origem.** Para o volume da Epicora sobra: são poucas máquinas por visita, e vários técnicos atrás do mesmo IP de cliente ainda ficariam longe do teto. Se estourar, o desfecho é o mesmo de estar sem rede — uma linha no log e o diagnóstico segue. É por isso que "falha nunca bloqueia" deixou de ser cortesia: com limite de requisição em jogo, virou requisito de funcionamento.

**Nenhum token, em nenhuma hipótese**, para levantar esse limite. Token embutido em binário público é token vazado, e o ADR-002 já registrou isso ao recusar a opção B.

## Por que uma tag fora do padrão não gera aviso

Só `vN.N.N` é comparada. Tag com sufixo de pré-release, encurtada ou com quatro componentes resolve "não verificada" e não diz nada na tela.

Parece excesso de zelo e não é: comparar `v1.2.0-rc1` com `1.2.0` produziria um aviso que o técnico não tem como interpretar, na frente do cliente. Aviso errado custa mais que aviso ausente — e o CI passou a recusar tag fora desse padrão, então o caso só aparece se alguém publicar release à mão.

## Consequências

1. **A tela 1 pode mudar de altura depois de aberta.** A caixa de aviso aparece quando a consulta responde, não quando a janela abre. É o preço de não travar a interface por 3 segundos na abertura.

2. **Um pacote de teste vai avisar que está desatualizado.** Pacote de CI carrega versão de desenvolvimento, não a da tag. O `LEIA-ME.txt` do pacote diz que isso é esperado.

3. **A verificação roda também em modo demonstração.** Rede não é a máquina do cliente, e revisar o texto do aviso é exatamente para o que a demonstração serve.

4. **Máquina sem rede não muda de comportamento** — o que já era o requisito do doc 01 §5: funciona sem internet depois de baixado.

## Revisão

Reabrir se: o ADR-002 voltar para repositório privado (o endpoint deixa de responder sem token), se o limite de 60/hora começar a aparecer no log de campo, ou se a direção decidir que a Epicora não quer que o GitHub registre quando a ferramenta roda — caso em que a saída é o arquivo estático em domínio próprio, e não desligar a verificação.
