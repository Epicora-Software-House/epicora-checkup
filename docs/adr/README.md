# Decisões de arquitetura (ADR)

Os nove pontos abertos da seção 11 do documento técnico. Uma decisão por arquivo.

**Regra:** nenhum destes é resolvido dentro do código por decisão individual de quem implementa. Se a implementação encontrar motivo para contrariar um ADR, o caminho é alterar o ADR — não contornar no código.

| # | Decisão | Estado | Bloqueia |
|---|---|---|---|
| [001](001-alvo-net-framework.md) | Alvo .NET Framework 4.7.2 | ✅ Aceita | — |
| [002](002-distribuicao-do-binario.md) | Repositório **público**, download via GitHub Releases | ✅ Aceita | — |
| [003](003-certificado-de-assinatura.md) | Certificado de assinatura de código OV / EV / nenhum | ⏸ **Pendente — direção** | Fase 3 |
| [004](004-nao-embutir-smartctl.md) | Não embutir `smartctl` na v1 | ✅ Aceita | — |
| [005](005-tabela-de-builds-do-windows.md) | Manter tabela de builds com `validUntil` | ✅ Aceita | — |
| [006](006-lista-de-cpus-windows-11.md) | Embutir lista de CPUs suportadas para Windows 11 | ✅ Aceita | — |
| [007](007-desativacao-de-item-de-inicializacao.md) | Mover entrada para chave própria, não escrever em `StartupApproved` | ✅ Aceita | Fase 5 |
| [008](008-idioma.md) | pt-BR apenas na v1 | ✅ Aceita | — |
| [009](009-prototipo-powershell-e-fallback-permanente.md) | Protótipo PowerShell é fallback permanente | ✅ Aceita | — |

## Estados

- **Aceita** — decidida, vale para o código.
- **Pendente** — aguarda decisão de quem tem alçada. Tem prazo e tem o que bloqueia.
- **Substituída** — foi revista. O arquivo permanece, com link para a que a substituiu. ADR não se apaga.
