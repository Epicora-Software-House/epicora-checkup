# ADR-010 — Projetos SDK-style e UI escrita em código

**Estado:** Aceita
**Data:** 2026-08-03
**Referência:** doc 02 §2 e §3.3; [ADR-001](001-alvo-net-framework.md); [ADR-008](008-idioma.md)

## Contexto

A Fase 2 começa e o formato dos projetos C# precisa ser escolhido antes de existir código, porque trocar depois é reescrever todos os `.csproj` e possivelmente todas as telas.

Duas restrições práticas moldaram a decisão:

1. **O desenvolvimento acontece em macOS.** .NET Framework 4.7.2 e WinForms não compilam em Mac, em nenhuma configuração. Não é questão de instalar ferramenta — o alvo é Windows-only. Quem escreve não pode compilar localmente, e o designer visual do Visual Studio é inacessível de qualquer forma.
2. **O [ADR-008](008-idioma.md) exige que nenhuma string de interface fique hardcoded no meio do código**, com os textos de UI num único ponto por projeto. O designer do WinForms faz o oposto: espalha literais dentro de `.Designer.cs` gerado.

## Decisão

**Todos os projetos em formato SDK-style. A UI do WinForms é escrita em C#, sem o designer visual.**

O alvo continua o do [ADR-001](001-alvo-net-framework.md) — .NET Framework 4.7.2, x64 — e continua fixado num só arquivo. Muda apenas o nome da propriedade:

| Formato | Propriedade |
|---|---|
| Legado, citado no exemplo do ADR-001 | `<TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>` |
| SDK-style, adotado aqui | `<TargetFramework>net472</TargetFramework>` |

Mesmo alvo. O exemplo do ADR-001 foi corrigido para não induzir a erro.

## Consequências

**A favor:**

- `.csproj` de dez linhas em vez de cem, sem GUID e sem `packages.config`. Escrevível e revisável à mão, o que importa quando quem escreve não tem como abrir o Visual Studio.
- Sem lista manual de `<Compile Include>`: arquivo novo entra pelo glob. Elimina a classe de erro "criei o arquivo e esqueci de adicionar ao projeto".
- Diff legível e sem conflito de merge em bloco gerado.
- `dotnet build` e `dotnet test` funcionam direto, o que é o que o CI usa.
- Textos de UI num `Strings.cs` único, como o ADR-008 pede, em vez de espalhados por `.Designer.cs`.

**Contra, e assumido:**

- **O designer visual do Visual Studio não abre telas SDK-style de net472 de forma confiável.** Quem preferir montar tela arrastando controle não vai conseguir. Montagem de layout passa a ser código.
- Layout em código é mais verboso para tela densa. A tela 3, que é a mais rica, vai custar mais linhas do que custaria no designer.

## O que fica registrado como ponto aberto

**Empacotamento em executável único.** O doc 01 §4 exige arquivo único sem dependências, e `EpicoraCheckup.Rules` depende de `Newtonsoft.Json` — net472 não traz `System.Text.Json`. Resolver isso é assunto da Fase 3, junto com a distribuição, e as opções conhecidas são ILRepack/ILMerge no CI ou embutir os assemblies como recurso com um handler de `AssemblyResolve`. **Não decidir dentro do código.**

**Arquivo de solução.** Não existe `.sln` comitado. Um `.sln` escrito à mão de um Mac, sem poder abri-lo para conferir, é risco sem retorno — GUID errado quebra o build de quem clonar. Gerar na primeira máquina Windows, onde é comando de um segundo:

```
dotnet new sln --name EpicoraCheckup
dotnet sln add src\EpicoraCheckup.Core\EpicoraCheckup.Core.csproj
dotnet sln add src\EpicoraCheckup.Rules\EpicoraCheckup.Rules.csproj
dotnet sln add tests\EpicoraCheckup.Rules.Tests\EpicoraCheckup.Rules.Tests.csproj
```

O CI não depende do `.sln`: compila o projeto de teste, que arrasta o grafo inteiro por referência.
