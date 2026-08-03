# Solução C# — Fase 2

Alvo: **.NET Framework 4.7.2, x64** ([ADR-001](../docs/adr/001-alvo-net-framework.md)), projetos **SDK-style** com UI escrita em código ([ADR-010](../docs/adr/010-projetos-sdk-style-e-ui-em-codigo.md)).

O alvo é fixado em `Directory.Build.props` e **nenhum projeto pode sobrescrevê-lo**. `tests/Directory.Build.props` importa este arquivo em vez de repetir valores.

## O que existe

| Projeto | Estado | Conteúdo |
|---|---|---|
| `EpicoraCheckup.Core` | ✅ | Contratos (`ICollector`, `CollectorResult`, `CollectionContext`), modelo (`Finding`, `Score`, enums) e orquestrador |
| `EpicoraCheckup.Rules` | ✅ | Motor de regras declarativo, lendo `rules/*.json` |
| `EpicoraCheckup.App` | ✅ | WinForms: telas 1, 2, 3, 4 e 7 |
| `EpicoraCheckup.Collectors` | ⬜ | Um coletor por domínio. **Depende do pré-voo** — porte do `.ps1` só depois do campo |
| `EpicoraCheckup.Reporting` | ⬜ | JSON, HTML, log |
| `EpicoraCheckup.Optimizers` | ⬜ | Fase 5 |
| `EpicoraCheckup.Consolidator` | ⬜ | Fase 4 |

As telas 5 e 6 são de otimização e pertencem à Fase 5. O fluxo é 1 → 2 → 3 → 4 → 7.

## Modo demonstração

Os coletores reais só podem ser portados depois do pré-voo, mas as telas e os textos de cliente precisam de revisão antes disso. Então:

```
EpicoraCheckup.exe --demonstracao tests\fixtures\sintetica-vermelha.json
```

Percorre o fluxo inteiro — as cinco telas, o motor de regras de verdade sobre a matriz de verdade — com os dados vindos de uma fixture. **Não coleta nada da máquina e não grava nenhum arquivo.**

Não gravar é a proteção, não um detalhe: um relatório derivado de fixture não pode circular, e depois que o arquivo existe nenhum aviso na tela impede alguém de entregá-lo. A faixa roxa em todas as telas é o segundo aviso, não o primeiro.

Sem `--demonstracao`, a ferramenta abre e explica que os coletores não foram portados. Não produz relatório vazio.

## O que o CI entrega para quem testa

O job `app` publica o artefato `EpicoraCheckup-teste`: o executável, as dependências, a pasta `rules/` e as três fixtures sintéticas, mais um `LEIA-ME.txt`. É uma **pasta**, não um arquivo único — ver pontos abertos.

O artefato depende do job `motor`: motor de regras vermelho não gera executável.

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

## Pontos abertos, registrados

**Executável único.** O doc 01 §4 exige arquivo único sem dependências, e `Rules` depende de `Newtonsoft.Json` porque net472 não traz `System.Text.Json`. Resolver é assunto da Fase 3 — ILRepack no CI, ou assemblies embutidos como recurso com handler de `AssemblyResolve`. Não decidir no código.

**`.sln`.** Não há solução comitada, de propósito — ver [ADR-010](../docs/adr/010-projetos-sdk-style-e-ui-em-codigo.md) para o comando que gera na primeira máquina Windows. O CI não depende dela.

**Timeout por coletor.** `CollectionOrchestrator` implementa o tempo limite com `Task.WhenAny` e **aceita que a thread do coletor travado fique órfã** — cancelar chamada WMI síncrona em andamento não é trivial em .NET (doc 02 §3.2, confiança M). O que está garantido é que a ferramenta segue adiante; o que não está é que a thread morra. Custo aceito: o inaceitável é a janela congelada na frente do cliente. **Ainda não foi exercitado contra WMI travado de verdade** — só contra coletor de fixture.

**Reflow da tela 3.** Os cartões têm largura fixa de 830 px e altura medida na montagem via `TextRenderer.MeasureText`. Redimensionar a janela não requebra o texto. A janela tem `MinimumSize` de 900, então nada é cortado, mas em tela grande sobra espaço à direita.

**Falso positivo não recalcula o score.** Marcar um achado registra que o técnico discorda da regra, para a regra ser corrigida. Se mexesse no número, o índice deixaria de medir a máquina e passaria a medir a opinião de quem operou a ferramenta. Decisão tomada no código e comentada em `Screen3Risks.MarkFalsePositive` — se o comercial quiser o contrário, é ADR.
