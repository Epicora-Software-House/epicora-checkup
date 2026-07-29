# ADR-001 — Alvo .NET Framework 4.7.2, x64

**Estado:** Aceita
**Data:** 2026-07-29
**Referência:** doc técnico §1 e §11 ponto 1

## Contexto

O documento técnico especifica .NET Framework 4.8 e registra a ressalva: Windows Server 2019 traz 4.7.2, não 4.8. Se houver qualquer chance de rodar em Server 2019, o alvo deve ser 4.7.2, que roda em ambos. Mudar depois é retrabalho.

A vertical de Rede da Epicora já inclui servidores no escopo comercial. Coleta de servidores está fora do escopo desta versão da ferramenta, mas é um caminho de evolução declarado no próprio documento funcional §10.

## Decisão

**Alvo: .NET Framework 4.7.2, arquitetura x64.**

## Consequências

- Roda em Windows 10 1903+, Windows 11, Windows Server 2019 e 2022 sem instalar runtime.
- Se servidores entrarem no escopo, não há retarget.
- Perde-se o que 4.8 adiciona sobre 4.7.2. Para o perfil desta aplicação — WMI, registro, WinForms, serialização JSON — a diferença é irrelevante.

## Implementação

Fixar em `Directory.Build.props` na raiz de `src/`, não projeto a projeto:

```xml
<TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
<PlatformTarget>x64</PlatformTarget>
```

Nenhum projeto pode sobrescrever o alvo individualmente.

## Ponto de atenção

Em Windows 11 ARM64 o executável x64 roda sob emulação. Ele *funciona*, mas os dados de hardware coletados em VM ARM não são representativos — ver `docs/adr/009` sobre validação em máquina física.
