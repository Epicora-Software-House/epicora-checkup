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

**Fase 1** validada em **2 máquinas** de 5–10, ambas notebooks. As duas rodadas renderam correções reais de coleta. O restante da validação foi **adiado por decisão registrada** ([ADR-012](docs/adr/012-ordem-porte-antes-do-campo.md)): o campo é retomado com o executável pronto, não com o protótipo.

O protótipo **não é descartável** — é o fallback permanente para quando o EDR de um cliente bloquear o executável ([ADR-009](docs/adr/009-prototipo-powershell-e-fallback-permanente.md)), e segue em sincronia com o schema.

**Fase 2** fecha o fluxo ponta a ponta: `Core`, `Rules`, os **16 coletores** portados do protótipo ([ADR-012](docs/adr/012-ordem-porte-antes-do-campo.md)), a **gravação** de JSON, HTML e log, e o executável WinForms com as telas 1, 2, 3, 4 e 7. O motor de regras é verificado contra os *golden files* em `tests/expected/`, a derivação dos coletores e o contrato do arquivo gravado têm testes próprios. Ver [`src/README.md`](src/README.md).

O CI publica o artefato `EpicoraCheckup-teste` com o executável, a matriz e as fixtures. **Não é assinado**, então o SmartScreen vai reclamar (ADR-003, esperado).

O **modo demonstração** continua existindo, para revisar telas e textos sem tocar na máquina e sem gravar arquivo:

```
EpicoraCheckup.exe --demonstracao fixtures\sintetica-vermelha.json
```

> **Pré-voo pendente, agora dos dois lados.** Nem as últimas mudanças do `.ps1` nem uma linha sequer do porte em C# rodaram em Windows — o desenvolvimento acontece em Mac e o CI compila, mas não executa contra WMI. **Nada que dependa de coleta é confiável até esse run acontecer.** O que já está verificado é o que não depende de máquina: compilação, derivação de campo e conformidade do JSON com o schema.

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

**Testes C#, em Windows** (os três projetos, com o `--settings` obrigatório):

```
dotnet test tests\EpicoraCheckup.Rules.Tests\EpicoraCheckup.Rules.Tests.csproj --settings tests\x64.runsettings
dotnet test tests\EpicoraCheckup.Collectors.Tests\EpicoraCheckup.Collectors.Tests.csproj --settings tests\x64.runsettings
dotnet test tests\EpicoraCheckup.Reporting.Tests\EpicoraCheckup.Reporting.Tests.csproj --settings tests\x64.runsettings
```

**Compilar no Mac** funciona, apesar do alvo ser .NET Framework: `Microsoft.NETFramework.ReferenceAssemblies` resolve as referências sem targeting pack instalado. `dotnet build` passa; `dotnet test` não roda, porque net472 precisa de Windows para executar.

O CI em [`.github/workflows/build.yml`](.github/workflows/build.yml) roda tudo a cada push. Ele é também a máquina de build do projeto, porque o desenvolvimento acontece em Mac.

## Regras de contribuição que não são negociáveis

1. **`Indeterminate` nunca vira `NonCompliant`.** Falha de coleta não é achado negativo.
2. **Nunca `Win32_Product`** — dispara reconfiguração de pacotes MSI na máquina do cliente.
3. **Nada de escrita fora da pasta de saída** até a Fase 5, e lá só com marcação individual do técnico.
4. **Regra sem `clientText` aprovado pelo comercial não entra em release.**
5. **Nenhum dado de cliente comitado sem passar por `tools/anonymize-fixture.mjs`.**
