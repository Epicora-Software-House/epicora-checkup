# Epicora Checkup

Coletor portátil de inventário e diagnóstico para estações Windows. Executável único, sem instalação, sem persistência, operado por técnico da Epicora presencialmente.

Produz, por máquina: um **inventário** completo, uma **lista de riscos** em linguagem de cliente com severidade e ação recomendada, e — a partir da Fase 5, só com autorização item por item — um conjunto de **otimizações seguras** com medição de antes e depois.

> **Não é** agente, não fica residente, não instala nada, não abre porta de rede, não faz telemetria.
>
> A única conversa com a internet é a **verificação de versão** na abertura: um `GET` ao release mais recente publicado, que não envia nada sobre a máquina, sobre o cliente ou sobre o diagnóstico, e que falha em silêncio sem consequência. Os termos exatos — inclusive o que o GitHub passa a registrar — estão no [ADR-014](docs/adr/014-verificacao-de-versao.md).

## Documentação

| Documento | Conteúdo |
|---|---|
| [`docs/01-especificacao-funcional.md`](docs/01-especificacao-funcional.md) | O que é, para que serve, fluxo de telas, princípios de projeto, o que a ferramenta **não** deve fazer, critérios de aceite, fases |
| [`docs/02-especificacao-tecnica.md`](docs/02-especificacao-tecnica.md) | Stack, arquitetura, fontes de dados WMI/registro com nível de confiança, schema JSON, distribuição e assinatura |
| [`docs/03-matriz-riscos-otimizacoes.md`](docs/03-matriz-riscos-otimizacoes.md) | Matriz de regras, modelo de score, textos de cliente, catálogo de otimizações |
| [`docs/adr/`](docs/adr/) | Decisões da Fase 0, uma por arquivo |
| [`docs/pre-voo.md`](docs/pre-voo.md) | **Roteiro do técnico** para a primeira rodada em 10 máquinas: link de download, o que fazer quando o SmartScreen ou o EDR reclamar, quais perfis de máquina cobrir e o que anotar |

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

**Fase 3** está fechada, nas três frentes que o doc 01 §11 pede:

- **Distribuição** ([ADR-013](docs/adr/013-executavel-unico.md)): o CI mescla os assemblies e embute a matriz, e o artefato `EpicoraCheckup-teste` traz **um arquivo de ~1 MB**, sem DLL nem pasta `rules/` ao lado. Em tag `v*` sai um release com URL estável de download.
- **Verificação de versão** ([ADR-014](docs/adr/014-verificacao-de-versao.md)): a tela 1 compara a própria versão com o release mais recente e avisa **sem bloquear**. Falha — sem rede, com proxy, no limite de 60 requisições/hora por IP da API não autenticada — dá uma linha no log e o diagnóstico segue.
- **Procedência do relatório**: `tool.version` e `tool.commit` são carimbados pelo CI, e `tool.rulesVersion` passa a identificar a matriz que avaliou, por data declarada mais impressão digital do conteúdo carregado ([ADR-015](docs/adr/015-versionamento-da-matriz.md)) — hoje `2026.08.12+6cd4167e`. Os três juntos respondem "qual versão, com qual matriz, produziu este número", que é a primeira pergunta de um achado contestado.

**Assinatura de código não entra**, e é decisão registrada, não pendência: o [ADR-003](docs/adr/003-certificado-de-assinatura.md) recusou certificado na v1. Consequência aceita: o SmartScreen reclama de aplicativo não reconhecido em toda máquina, e o SHA-256 publicado ao lado do release é o que permite conferir a origem do arquivo.

O **modo demonstração** continua existindo, para revisar telas e textos sem tocar na máquina e sem gravar arquivo:

```
EpicoraCheckup.exe --demonstracao fixtures\sintetica-vermelha.json
```

> **Pré-voo pendente, agora dos dois lados.** Nem as últimas mudanças do `.ps1` nem uma linha sequer do porte em C# rodaram em Windows — o desenvolvimento acontece em Mac e o CI compila, mas não executa contra WMI. **Nada que dependa de coleta é confiável até esse run acontecer.** O que já está verificado é o que não depende de máquina: compilação, derivação de campo e conformidade do JSON com o schema.
>
> O roteiro que fecha essa pendência está em [`docs/pre-voo.md`](docs/pre-voo.md) — é o documento que vai para o técnico, com o link de download, os avisos do Windows que são esperados, os perfis de máquina a cobrir e o que anotar em cada uma.

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

**Testes C#, em Windows** (os dois projetos, com o `--settings` obrigatório):

```
dotnet test tests\EpicoraCheckup.Rules.Tests\EpicoraCheckup.Rules.Tests.csproj --settings tests\x64.runsettings
dotnet test tests\EpicoraCheckup.Collectors.Tests\EpicoraCheckup.Collectors.Tests.csproj --settings tests\x64.runsettings
```

**Compilar no Mac** funciona, apesar do alvo ser .NET Framework: `Microsoft.NETFramework.ReferenceAssemblies` resolve as referências sem targeting pack instalado. `dotnet build` passa; `dotnet test` não roda, porque net472 precisa de Windows para executar.

O CI em [`.github/workflows/build.yml`](.github/workflows/build.yml) roda tudo a cada push. Ele é também a máquina de build do projeto, porque o desenvolvimento acontece em Mac.

## Regras de contribuição que não são negociáveis

1. **`Indeterminate` nunca vira `NonCompliant`.** Falha de coleta não é achado negativo.
2. **Nunca `Win32_Product`** — dispara reconfiguração de pacotes MSI na máquina do cliente.
3. **Nada de escrita fora da pasta de saída** até a Fase 5, e lá só com marcação individual do técnico.
4. **Regra sem `clientText` aprovado pelo comercial não entra em release.**
5. **Nenhum dado de cliente comitado sem passar por `tools/anonymize-fixture.mjs`.**
