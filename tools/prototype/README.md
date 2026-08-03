# Protótipo PowerShell — procedimento de campo

Dois scripts com propósitos distintos. **Não é código descartável:** é o fallback permanente para quando o EDR de um cliente bloquear o executável ([ADR-009](../../docs/adr/009-prototipo-powershell-e-fallback-permanente.md)).

| Script | Para quê | Quando rodar |
|---|---|---|
| `Test-DataSources.ps1` | **Sonda.** Executa cada fonte de confiança M ou B e grava, em bruto, o que ela devolveu | Fase 1, uma vez por máquina do lote de validação |
| `Invoke-EpicoraCheckup.ps1` | **Coletor.** Produz o JSON do diagnóstico no schema 1.0 | Toda visita, sempre que o `.exe` não puder rodar |
| `EpicoraCheckup.bat` | Chama o coletor pedindo os dados de identificação, contornando a ExecutionPolicy | É por aqui que o técnico começa |

## Um arquivo, de propósito

O coletor é **um único `.ps1` autocontido**, não uma pasta de módulos. Dois motivos:

1. O valor do fallback é ser **auditável na hora**. O TI do cliente que se recusa a liberar um binário desconhecido pode abrir um arquivo e ler o que ele faz. Uma pasta com quinze módulos anula isso.
2. Copiar um arquivo para a máquina do cliente é trivial; copiar uma árvore de diretórios, não.

Cada coletor é uma chamada isolada de `Invoke-Collector` com o mesmo contrato do `ICollector` do C# (`Id`, `DisplayName`, `RequiresElevation`, `Collect`). A Fase 2 é porte função a função, não reescrita.

## Requisitos

- **Windows PowerShell 5.1** — presente em toda instalação de Windows desde o 10. Não requer PowerShell 7, não instala nada.
- Elevação é **opcional**, e custa menos do que se supunha. A sonda mediu que só três fontes exigem privilégio: **TPM** (`Win32_Tpm`), **BitLocker** (`Win32_EncryptableVolume`) e **SMART** (`MSStorageDriver_FailurePredictStatus`). Nenhum coletor é ignorado por falta de elevação — cada uma dessas três degrada para `null` isoladamente e as regras que dependem delas resolvem `Indeterminate`. Tudo o mais, inclusive antivírus, ativação, firewall, SMBv1, RDP, Secure Boot e tipo de mídia do disco, responde sem elevação.

## Como rodar

**Modo normal, com perguntas:**

```bat
EpicoraCheckup.bat
```

**Direto, quando os dados já são conhecidos:**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Invoke-EpicoraCheckup.ps1 `
    -Technician "Gabriel" -Client "Cliente X" -DiagnosticId "DIAG-2026-014" `
    -MachineLabel "ADM-04" -Responsible "Maria" -Department "Administrativo" `
    -CorporateEnvironment
```

**Sonda, na Fase 1 — rodar as DUAS vezes:**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Test-DataSources.ps1
# depois, numa janela como Administrador:
powershell -NoProfile -ExecutionPolicy Bypass -File .\Test-DataSources.ps1
```

A diferença entre as duas saídas é o que define `RequiresElevation` de cada coletor na Fase 2. Sem as duas, isso vira chute.

## O que os scripts fazem e não fazem

**Fazem:** leem metadados de hardware, sistema, software, rede e configuração de segurança, via WMI/CIM e registro.

**Não fazem, em nenhuma circunstância:** ler conteúdo de arquivo, e-mail, mensagem, histórico ou favoritos de navegador · coletar credencial ou chave de produto · capturar tela, teclado ou áudio · listar nomes de arquivos pessoais · enviar dado para servidor nenhum · criar serviço, tarefa agendada ou item de inicialização · alterar qualquer configuração da máquina.

**Escrita em disco:** apenas o JSON e o log, na pasta de saída. A sonda também escreve, em pasta temporária, o HTML do `powercfg /batteryreport`, que é lido e apagado em seguida — pule com `-SkipBatteryReport` se o cliente exigir zero escrita.

`tools/check-prototype.mjs` verifica essas proibições a cada mudança:

```sh
node tools/check-prototype.mjs
```

## Depois de rodar

```sh
# 1. avaliar contra a matriz de regras, no Mac do analista
node tools/evaluate-rules.mjs <saida>.json
node tools/evaluate-rules.mjs <saida>.json --incluir-pendentes    # conferência da matriz

# 2. anonimizar ANTES de virar fixture comitada — obrigatório
node tools/anonymize-fixture.mjs <saida>.json tests/fixtures/<nome>.json

# 3. conferir que o JSON está no schema
node tools/validate-schema.mjs
```

## Quando o antivírus bloquear

Vai acontecer, e é justamente para isso que este script existe. Na ordem:

1. Mostre o `.ps1` ao responsável de TI do cliente — ele é texto, legível, e é isso que muda a conversa.
2. Se ainda assim a ExecutionPolicy bloquear, `-ExecutionPolicy Bypass` já está no `.bat` e vale só para aquele processo. Não altera a política da máquina.
3. Se a política for imposta por GPU/GPO e o Bypass não passar, peça exceção temporária ao TI.
4. Se negarem, registre no diagnóstico como *"máquina não pôde ser avaliada — coleta bloqueada por política"*. Não insista e não contorne.

## Limitações conhecidas nesta versão

Todas deliberadas, todas registradas:

| Campo | Estado | Porquê |
|---|---|---|
| `cpu.win11Supported` | sempre `null` | lista oficial não embutida — [ADR-006](../../docs/adr/006-lista-de-cpus-windows-11.md) |
| `os.buildFreshness.evaluated` | sempre `false` | tabela de builds vazia — [ADR-005](../../docs/adr/005-tabela-de-builds-do-windows.md) |
| `events.evaluated` | sempre `false` | IDs já levantados na doc oficial, mas `validUntil` de `rules/event-ids.json` segue nulo até a validação de campo — a sonda precisa rodar com filtro de provedor numa máquina de histórico conhecido |
| `antivirus.*.interpretation.confidence` | sempre `None` | `productState` é máscara não documentada; decodificar exige dados de campo |
| `storage.systemDisk.trimEnabled` | **medido** | via `fsutil behavior query`, que responde sem elevação; padrão ancorado no número porque a saída é localizada |
| `storage.systemDisk.fragmentationPercent` | sempre `null` | análise de volume é lenta demais para a meta de 90 s |
| SMART detalhado | ausente | [ADR-004](../../docs/adr/004-nao-embutir-smartctl.md) |
| `battery.wearPercent` e `cycleCount` | **medidos** | `root\wmi` (`BatteryCycleCount`, `BatteryFullChargedCapacity`) + `Win32_PortableBattery.DesignCapacity`, validados contra `powercfg /batteryreport` na mesma máquina. Sem escrever arquivo. **Não** multiplicar por `CapacityMultiplier` |

Cada uma dessas faz a regra correspondente resolver `Indeterminate` e aparecer no bloco *"não foi possível verificar"* do relatório. **Nenhuma vira achado negativo.**
