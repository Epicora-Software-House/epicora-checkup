# Solução C# — Fase 2

Alvo: **.NET Framework 4.7.2, x64** ([ADR-001](../docs/adr/001-alvo-net-framework.md)), projetos **SDK-style** com UI escrita em código ([ADR-010](../docs/adr/010-projetos-sdk-style-e-ui-em-codigo.md)).

O alvo é fixado em `Directory.Build.props` e **nenhum projeto pode sobrescrevê-lo**. `tests/Directory.Build.props` importa este arquivo em vez de repetir valores.

## O que existe

| Projeto | Estado | Conteúdo |
|---|---|---|
| `EpicoraCheckup.Core` | ✅ | Contratos (`ICollector`, `CollectorResult`, `CollectionContext`), modelo (`Finding`, `Score`, enums) e orquestrador |
| `EpicoraCheckup.Rules` | ✅ | Motor de regras declarativo, lendo `rules/*.json` |
| `EpicoraCheckup.App` | ✅ | WinForms: telas 1, 2, 3, 4 e 7 |
| `EpicoraCheckup.Collectors` | ✅ | Os 16 coletores portados do `.ps1` (ADR-012), mais a consolidação dos campos derivados |
| `EpicoraCheckup.Reporting` | ✅ | Documento do schema 1.0, relatório HTML autocontido e log de execução |
| `EpicoraCheckup.Optimizers` | ⬜ | Fase 5 |
| `EpicoraCheckup.Consolidator` | ⬜ | Fase 4 |

As telas 5 e 6 são de otimização e pertencem à Fase 5. O fluxo é 1 → 2 → 3 → 4 → 7.

## Coletores

Um por domínio, na ordem em que rodam (`WindowsCollectorSet`). Cada arquivo tem duas partes, e a separação é o que torna o porte testável:

- **O coletor** lê as fontes — WMI, registro, `fsutil`, assinatura de arquivo. Não é testado por teste automatizado: fonte se exercita em campo, com a sonda.
- **`*Facts`** decide o que aquilo significa, a partir de `PropertyBag` — o retrato de uma instância, já desconectado da fonte. É função pura, roda em qualquer máquina, e é onde estão os testes.

Três coisas que não são óbvias e que mordem quem mexer:

1. **`Payload.Sanitized` não é cosmético.** Atribuir um `string` nulo a um `JObject` produz um token de tipo `String` com conteúdo nulo — não um token nulo. Serializado dá no mesmo; em memória, não: o motor decide disponibilidade por `Type == JTokenType.Null` e passaria a tratar campo não coletado como campo preenchido. Uma falha de coleta viraria achado avaliado em vez de `Indeterminate`.
2. **`RequiresElevation` é `false` em quase tudo, e isso foi medido.** Só TPM, BitLocker e SMART exigem privilégio, e as três degradam para null isoladamente. Marcar um coletor inteiro descartaria de graça a família de achados mais valiosa em toda visita sem senha de administrador.
3. **A consolidação roda depois da coleta, não dentro dela.** `Consolidation.Apply` preenche o que depende de mais de um coletor — o cruzamento antivírus × software e a elegibilidade de Windows 11. Acoplar coletores entre si faria o tempo limite de um derrubar o outro.

## Modo demonstração

```
EpicoraCheckup.exe --demonstracao tests\fixtures\sintetica-vermelha.json
```

Percorre o fluxo inteiro — as cinco telas, o motor de regras de verdade sobre a matriz de verdade — com os dados vindos de uma fixture. **Não coleta nada da máquina e não grava nenhum arquivo.**

Não gravar é a proteção, não um detalhe: um relatório derivado de fixture não pode circular, e depois que o arquivo existe nenhum aviso na tela impede alguém de entregá-lo. A faixa roxa em todas as telas é o segundo aviso, não o primeiro.

A consolidação também não roda em demonstração: a fixture já vem consolidada, e reprocessá-la sobrescreveria o cenário gravado que os golden files esperam.

Sem `--demonstracao`, a ferramenta coleta desta máquina e grava em `.\EpicoraCheckup\<CLIENTE>\`.

## O que o CI entrega para quem testa

O job `app` publica o artefato `EpicoraCheckup-teste`: **o `EpicoraCheckup.exe` sozinho**, o SHA-256 dele, um `LEIA-ME.txt` e as três fixtures sintéticas — que só servem ao modo demonstração. O executável não precisa de nada ao lado (ADR-013).

O job confere isso antes de montar o pacote: se sobrar qualquer `.dll` no publish, ou se o executável for pequeno demais para conter a matriz, o build falha. Um exe que depende de DLL ao lado roda no runner e quebra na máquina do cliente, que é o pior lugar para descobrir.

Em tag `v*`, o job `release` publica o executável e o hash num release do GitHub. É o que dá a URL estável do doc 02 §8.1 — `releases/latest/download/EpicoraCheckup.exe` —, que resolve sempre para o binário mais recente e permite guiar o técnico por telefone com um link só.

O artefato depende dos jobs `motor` e `coletores`: motor de regras ou coletor vermelho não gera executável.

**O `.exe` não é assinado** (ADR-003), então o SmartScreen vai reclamar de aplicativo não reconhecido em toda máquina. O doc 02 §8.4 diz que isso vai acontecer, não que pode. Para testador interno dá para prosseguir; não é caminho liso para cliente.

**Regra de dependência** (doc 02 §2): `Collectors`, `Rules`, `Optimizers` e `Reporting` dependem de `Core`. Nenhum depende de `App`. **Nenhum referencia WinForms.** É o que permite testar sem UI e reaproveitar no consolidador.

## Compilar e testar

Não compila em macOS: .NET Framework e WinForms são Windows-only. Numa máquina Windows, ou no CI:

```
dotnet test tests\EpicoraCheckup.Rules.Tests\EpicoraCheckup.Rules.Tests.csproj --settings tests\x64.runsettings
```

O `--settings` não é opcional: o vstest escolhe x86 por padrão para .NET Framework e falha ao carregar assembly x64 com uma mensagem que não aponta para a causa.

O CI em `.github/workflows/build.yml` faz isso a cada push — e é a máquina de build do projeto, já que o desenvolvimento acontece em Mac.

## O contrato do motor de regras

`EpicoraCheckup.Rules` não é livre para produzir o que quiser. A saída serializada tem que bater, campo por campo, com:

| Contrato | Arquivo | Cenário |
|---|---|---|
| Só regras habilitadas | blocos `findings` e `score` dentro de `tests/fixtures/sintetica-*.json` | O que a ferramenta produz hoje |
| Matriz completa, 61 regras | `tests/expected/sintetica-*.matriz-completa.json` | Pega regressão em regra ainda não habilitada |

Os dois foram gerados pelo motor de referência em `tools/evaluate-rules.mjs`. **Quando o motor C# passar nos três, o de referência é aposentado** — ele é instrumento, não segundo sistema.

Três detalhes do contrato que não são óbvios e que já mordem quem refatora:

1. **A ordem de carga dos arquivos de regra é parte da saída.** `Score.VerdictDrivenBy` preserva a ordem de carga — ordinal por nome de arquivo — e não a ordem de exibição, que é ordenada por severidade depois.
2. **Ausente e nulo-explícito são estados diferentes.** `equals` contra `null` é verdadeiro para um campo nulo e falso para um campo ausente. Por isso existe o tipo `Missing` em vez de usar `null`.
3. **`notContains` sobre valor que não é texto nem lista devolve falso**, não verdadeiro. Assimetria herdada do motor de referência, e correta: não se afirma "não contém" sobre algo que não pôde ser lido.

## Um documento, dois usos

`CheckupDocument.Build` monta o documento do schema 1.0, e ele é usado **duas vezes**: sem `findings`, é a entrada do motor de regras na tela 2; com eles, é o arquivo que o consolidador lê.

Isso não é economia de código, é correção. OS-004 lê `manual.corporateEnvironment`, que não está dentro de coletor nenhum — avaliar sobre um documento que só tem `collectors` faz a regra perder a marcação do técnico **em silêncio**, sem erro e sem teste vermelho. Foi exatamente o bug da primeira versão, e `ReportingTests.Marcacao_de_ambiente_corporativo_chega_ate_OS004` existe para ele não voltar.

Como só `corporateEnvironment` alimenta regra, e ele vem da tela 1, avaliar na tela 2 — antes da tela 4 — não perde nada.

## A saída é verificada contra o schema

Não há validador de JSON Schema gratuito e decente para net472, então quem confere é o `ajv` que já cobre as fixtures. Os testes gravam amostras em `tests/generated/` e o CI roda `tools/validate-schema.mjs` em cima delas. Sem esse passo, "monta o documento do schema" seria afirmação sem verificação.

## Pontos abertos, registrados

**Nada disto rodou em Windows ainda.** Os coletores compilam, e a derivação e a gravação têm teste, mas nenhuma linha do porte tocou WMI de verdade. É o mesmo estado do `.ps1` desde os últimos achados de campo, e é o que o pré-voo resolve — ver o README da raiz.

**`tool.rulesVersion` e `tool.commit` saem nulos.** O primeiro exige versionar a matriz; o segundo, o CI carimbar o commit no assembly. Os dois entram na Fase 3, junto com a publicação em release.

**Executável único — resolvido ([ADR-013](../docs/adr/013-executavel-unico.md)).** O `publish` em Release mescla os quatro assemblies e o `Newtonsoft.Json` dentro do `.exe` com ILRepack, e a matriz de regras viaja embutida como recurso. Sai um arquivo de ~1 MB, sem `.exe.config` e sem pasta `rules/` ao lado. O `build` normal continua produzindo as DLLs soltas, porque depurar binário mesclado é pior e em desenvolvimento não há motivo para pagar isso.

Uma pasta `rules/` ao lado do executável **tem precedência** sobre a matriz embutida — é o que atende o doc 02 §3.5, que exige trocar regra sem recompilar. O log registra de onde a matriz veio em toda execução: com sobreposição, "qual matriz produziu este número" deixa de ter resposta óbvia, e é a primeira pergunta de um achado contestado.

**`.sln`.** Não há solução comitada, de propósito — ver [ADR-010](../docs/adr/010-projetos-sdk-style-e-ui-em-codigo.md) para o comando que gera na primeira máquina Windows. O CI não depende dela.

**Timeout por coletor.** `CollectionOrchestrator` implementa o tempo limite com `Task.WhenAny` e **aceita que a thread do coletor travado fique órfã** — cancelar chamada WMI síncrona em andamento não é trivial em .NET (doc 02 §3.2, confiança M). O que está garantido é que a ferramenta segue adiante; o que não está é que a thread morra. Custo aceito: o inaceitável é a janela congelada na frente do cliente. **Ainda não foi exercitado contra WMI travado de verdade** — só contra coletor de fixture.

**Reflow da tela 3.** Os cartões têm largura fixa de 830 px e altura medida na montagem via `TextRenderer.MeasureText`. Redimensionar a janela não requebra o texto. A janela tem `MinimumSize` de 900, então nada é cortado, mas em tela grande sobra espaço à direita.

**Falso positivo não recalcula o score.** Marcar um achado registra que o técnico discorda da regra, para a regra ser corrigida. Se mexesse no número, o índice deixaria de medir a máquina e passaria a medir a opinião de quem operou a ferramenta. Decisão tomada no código e comentada em `Screen3Risks.MarkFalsePositive` — se o comercial quiser o contrário, é ADR.
