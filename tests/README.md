# Testes

## `fixtures/` — o ativo de teste mais valioso do projeto

JSONs de coleta que servem ao mesmo tempo de entrada e de saída esperada. O acervo cresce a cada diagnóstico feito.

**Nenhum JSON de máquina real entra aqui sem passar por `tools/anonymize-fixture.mjs`.** O repositório vai para o GitHub e os dados são de cliente. `.gitignore` bloqueia `*-raw.json` justamente para forçar a etapa.

### As três fixtures sintéticas

Escritas à mão para exercitar caminhos que máquinas reais dificilmente cobrem todos de uma vez.

| Fixture | Cenário | O que exercita |
|---|---|---|
| `sintetica-verde.json` | Notebook 2025, SSD NVMe, 16 GB, Win11 24H2, BitLocker, Defender ativo, em domínio | **Nenhum achado.** É o teste anti-falso-positivo: as 61 regras rodam e nada dispara |
| `sintetica-amarela.json` | Desktop 2021, SSD SATA 240 GB, 8 GB com slot livre, **execução SEM elevação** | `Skipped` por falta de privilégio, `Failed` por tempo limite, e a propagação para `Indeterminate` |
| `sintetica-vermelha.json` | Desktop 2014, HDD com falha prevista, 4 GB, Win10 Home, sem TPM, usuário admin, SMBv1 | Achados críticos, veredito **Substituir**, score no piso |

Resultado com a matriz completa:

| Fixture | Não conforme | Indeterminado | Conforme | Score | Veredito |
|---|---|---|---|---|---|
| verde | 0 | 1 | 60 | 100 Verde | Manter |
| amarela | 11 | 27 | 23 | 32 | Fazer upgrade |
| vermelha | 36 | 10 | 15 | 0 | Substituir |

O bloco `findings` e `score` gravado dentro de cada fixture reflete **apenas as 5 regras habilitadas hoje** — que é o que a ferramenta produz de verdade. A saída da matriz inteira fica em `expected/`.

### Regenerar depois de mexer nas regras

```sh
node tools/evaluate-rules.mjs tests/fixtures/sintetica-verde.json --json
node tools/validate-schema.mjs
```

## `EpicoraCheckup.Rules.Tests/` — os testes do motor C#

Dois contratos por fixture, e os dois são verificados:

| Teste | Saída esperada |
|---|---|
| `Apenas_regras_habilitadas_reproduz_o_bloco_da_fixture` | os blocos `findings`/`score` gravados dentro da própria fixture |
| `Matriz_completa_reproduz_o_golden_file` | `expected/sintetica-*.matriz-completa.json` |

Mais `SemanticaDoMotorTests`, que cobre o que os golden files não cobrem: os cantos onde um refactor divergiria em silêncio — ausente versus nulo-explícito, o guard de `indeterminateWhen`, a assimetria de `notContains`, os limites das faixas do semáforo.

Os testes leem a matriz e as fixtures **reais** do repositório, não cópias embutidas. É deliberado: quem edita uma regra e esquece de regenerar os golden files precisa ver o teste ficar vermelho.

```
dotnet test tests\EpicoraCheckup.Rules.Tests\EpicoraCheckup.Rules.Tests.csproj --settings tests\x64.runsettings
```

Só roda em Windows — .NET Framework 4.7.2 não compila em macOS. O CI faz isso a cada push.

## `EpicoraCheckup.Collectors.Tests/` — a derivação dos coletores

O que se testa aqui é **o que a ferramenta conclui a partir do que a máquina respondeu**, não a leitura das fontes. WMI, registro e `fsutil` não são simulados: fonte se exercita em campo, com a sonda, que é o instrumento certo para isso.

Por isso cada coletor tem a parte que lê separada da parte que decide. A segunda recebe um `PropertyBag` montado à mão — com os tipos que WMI entrega de verdade, texto onde a classe declara texto — e roda em qualquer máquina.

A cobertura segue os achados de campo, porque é onde os defeitos apareceram:

| Teste | Achado que ele impede de voltar |
|---|---|
| `ArmazenamentoTests` | SMART perdido em máquina com dois discos (JULIA-LAPTOP), e leitura atribuída ao disco errado |
| `ContasTests` | `isLocalAdmin` falso-negativo com token filtrado do UAC (DELL-G15, SEC-007) |
| `RedeTests` | `Wired` inalcançável sem `NdisPhysicalMedium` |
| `FonteDeDadosTests` | campo de texto ausente chegando ao motor como campo preenchido |
| `BateriaTests` | desgaste negativo, e dado de uma bateria atribuído à outra |
| `ConsolidacaoTests` | "sem antivírus" para quem tem EDR, e `Unknown` contando como reprovado no Windows 11 |

## `EpicoraCheckup.Reporting.Tests/` — o contrato do arquivo gravado

JSON fora do schema 1.0 não entra no consolidador, e o erro só apareceria semanas depois, no escritório, com a visita encerrada. Então: blocos exatos do schema, datas com offset de fuso, UTF-8 sem BOM, campo opcional vazio como `null` e não como string vazia, nome de arquivo sanitizado com fallback.

Do relatório HTML, verifica-se o que é requisito e não estética: autocontido (sem `http`, sem `<script>`), imprimível (`@media print`), texto de fora escapado, e a separação entre risco e "não foi possível verificar".

## `expected/` — contrato de aceite do motor C#

`sintetica-<cor>.matriz-completa.json` é a saída do motor de referência em Node com **todas as 61 regras**, incluindo as pendentes.

Serve a dois propósitos:

1. **Conferência da matriz agora**, antes de existir uma linha de C#: dá para revisar regra por regra o que cada uma produziria numa máquina conhecida.
2. **Contrato da Fase 2**: o motor C# tem que produzir exatamente isto. Quando passar nos três, o motor Node é aposentado — ele é instrumento, não segundo sistema.

## `probes/` — saída bruta de `Test-DataSources.ps1`

Um JSON por máquina, por nível de privilégio. É o acervo que decide, empiricamente, quais das 56 regras pendentes podem ser habilitadas.

Ignorado pelo git por padrão: sonda contém dado de máquina real e ainda não passa pelo anonimizador.

## Achado de calibração já registrado

A fixture **amarela** foi desenhada para ser Amarelo e sai **32/100, Vermelho** sob a matriz completa. É exatamente o risco que o doc 03 §3 antecipou:

> *"Sinal de que estão errados: se todas as máquinas de um cliente saem Vermelho, o relatório perde poder de discriminação e o cliente para de acreditar."*

Com 36 regras somando peso, uma máquina ruim chega a zero e uma máquina mediana chega a vermelho. **Os pesos precisam ser recalibrados depois das dez primeiras máquinas reais**, e o modelo de soma linear com piso em zero provavelmente precisa mudar junto — talvez peso por categoria, com teto por categoria, em vez de soma livre.

Não é bug de implementação. É a calibração inicial fazendo o que o documento disse que ela faria, e agora está medida em vez de suposta.
