# Epicora Checkup

Coletor portátil de inventário e diagnóstico para estações Windows. Executável único, sem instalação, sem persistência, operado por técnico da Epicora presencialmente.

Produz, por máquina: um **inventário** completo, uma **lista de riscos** em linguagem de cliente com severidade e ação recomendada, e — a partir da Fase 5, só com autorização item por item — um conjunto de **otimizações seguras** com medição de antes e depois.

> **Não é** agente, não fica residente, não instala nada, não abre porta de rede, não faz telemetria.

## Documentação

| Documento | Conteúdo |
|---|---|
| [`docs/01-especificacao-funcional.md`](docs/01-especificacao-funcional.md) | O que é, para que serve, fluxo de telas, princípios de projeto, o que a ferramenta **não** deve fazer, critérios de aceite, fases |
| [`docs/02-especificacao-tecnica.md`](docs/02-especificacao-tecnica.md) | Stack, arquitetura, fontes de dados WMI/registro com nível de confiança, schema JSON, distribuição e assinatura |
| [`docs/03-matriz-riscos-otimizacoes.md`](docs/03-matriz-riscos-otimizacoes.md) | Matriz de regras, modelo de score, textos de cliente, catálogo de otimizações |
| [`docs/adr/`](docs/adr/) | Decisões da Fase 0, uma por arquivo |

Os três documentos de especificação são a fonte de verdade sobre **o que o produto é e o que ele não pode fazer**. Divergência entre código e documento é bug de um dos dois — resolver, não contornar.

Sobre **decisões**, quem manda é `docs/adr/`. Os três documentos são registros datados da versão 1.0 e listam vários pontos como "pendente de decisão"; o [índice de ADRs](docs/adr/README.md) diz o que foi decidido desde então. Quando os dois discordarem, o ADR vence.

## Estrutura

```
docs/       Especificação e decisões
schema/     Modelo de dados: JSON Schema + mapeamento campo → decisão comercial
rules/      Matriz de regras, declarativa e versionada
tools/      Protótipo PowerShell (fallback permanente) e utilitários Node
tests/      Fixtures, golden files do motor e testes C#
src/        Solução C# — ver src/README.md
```

## Estado atual

**Fase 0** fechada: dez decisões registradas em [`docs/adr/`](docs/adr/), schema 1.0 congelado, cada campo mapeado à decisão comercial que sustenta.

**Fase 1** em campo. O coletor PowerShell cobre os 16 domínios e foi sondado numa máquina; a meta da fase é 5–10. O protótipo **não é descartável** — é o fallback permanente para quando o EDR de um cliente bloquear o executável ([ADR-009](docs/adr/009-prototipo-powershell-e-fallback-permanente.md)).

**Fase 2** em andamento: `Core`, `Rules` e o executável WinForms com as telas 1, 2, 3, 4 e 7 existem, e o motor de regras é verificado contra os *golden files* em `tests/expected/`. Faltam os **coletores** — que dependem do pré-voo — e a gravação de JSON/HTML/log. Ver [`src/README.md`](src/README.md).

Até os coletores existirem, a ferramenta roda em **modo demonstração**, que percorre as telas com dados de uma fixture e não grava arquivo nenhum:

```
EpicoraCheckup.exe --demonstracao fixtures\sintetica-vermelha.json
```

O CI publica o artefato `EpicoraCheckup-teste` com o executável, a matriz e as fixtures — é o pacote para quem for testar as telas. **Não é assinado**, então o SmartScreen vai reclamar (ADR-003, esperado).

> **Pré-voo pendente.** As últimas mudanças no `.ps1` nasceram de dado de campo, mas **nunca rodaram em Windows**. Nada que dependa do coletor é confiável até esse run acontecer.

## Como rodar o que já existe

**Protótipo, na máquina Windows** (PowerShell 5.1, sem instalar nada):

```bat
tools\prototype\EpicoraCheckup.bat
```

**Validar schema, regras e protótipo, no Mac** (Node 18+):

```sh
npm ci
npm run check
node tools/evaluate-rules.mjs tests/fixtures/sintetica-vermelha.json
```

**Motor de regras C#, só em Windows** — .NET Framework não compila em macOS:

```
dotnet test tests\EpicoraCheckup.Rules.Tests\EpicoraCheckup.Rules.Tests.csproj --settings tests\x64.runsettings
```

O CI em [`.github/workflows/build.yml`](.github/workflows/build.yml) roda os dois a cada push. Ele é também a máquina de build do projeto, porque o desenvolvimento acontece em Mac.

## Regras de contribuição que não são negociáveis

1. **`Indeterminate` nunca vira `NonCompliant`.** Falha de coleta não é achado negativo.
2. **Nunca `Win32_Product`** — dispara reconfiguração de pacotes MSI na máquina do cliente.
3. **Nada de escrita fora da pasta de saída** até a Fase 5, e lá só com marcação individual do técnico.
4. **Regra sem `clientText` aprovado pelo comercial não entra em release.**
5. **Nenhum dado de cliente comitado sem passar por `tools/anonymize-fixture.mjs`.**
