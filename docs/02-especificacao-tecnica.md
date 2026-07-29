# Epicora Checkup — Especificação Técnica

**Versão do documento:** 1.0
**Data:** 29/07/2026
**Público-alvo:** equipe de desenvolvimento
**Documento irmão:** `01-especificacao-funcional.md`, `03-matriz-riscos-otimizacoes.md`

---

## Aviso de confiabilidade deste documento

Este documento contém nomes de classes WMI, caminhos de registro e cmdlets. Cada item da tabela de fontes de dados (seção 4) tem uma **coluna de confiança**:

| Marca | Significado | O que fazer |
|---|---|---|
| **A** | Alta confiança. Classe/caminho clássico e estável. | Implementar. Testar normalmente. |
| **M** | Confiança média. O nome e o comportamento devem ser conferidos na documentação atual da Microsoft antes de codificar. | **Conferir na doc oficial primeiro.** |
| **B** | Baixa confiança. Sei que o dado é obtenível, mas não afirmo o caminho exato. | Pesquisar e prototipar antes de estimar prazo. |

Nada marcado **M** ou **B** deve ser implementado a partir deste documento sem verificação na documentação oficial. Não copie nome de classe ou propriedade daqui de memória para dentro do código.

---

## 1. Decisão de stack

**C# + WinForms sobre .NET Framework 4.8**, arquitetura x64, executável único.

### Justificativa

| Critério | Por que essa escolha |
|---|---|
| Runtime já presente | .NET Framework 4.8 vem incluído a partir do Windows 10 1903 e no Windows 11 21H2; 4.8.1 a partir do Windows 11 22H2. Confirmado na documentação da Microsoft. |
| Tamanho do binário | 1–3 MB. Download rápido na máquina do cliente e menor superfície de suspeita para antivírus. |
| UI | WinForms *é* a interface nativa clássica do Windows que o produto pede. Não é limitação, é a escolha correta. |
| Acesso a WMI/CIM | `System.Management` nativo, sem dependência externa. |
| Manutenção | Stack estável, sem breaking changes previstos. |

### Alternativa avaliada e descartada

.NET 8 self-contained: gera executável acima de 60 MB. Download mais lento e perfil muito mais propenso a bloqueio por EDR. Descartado para esta finalidade.

### Ressalva importante

Se **servidores** entrarem no escopo em algum momento: Windows Server 2019 traz .NET Framework 4.7.2, não 4.8. Se houver qualquer chance de rodar em Server 2019, o alvo do projeto deve ser **4.7.2**, que roda em ambos. A decisão precisa ser tomada na Fase 0, porque mudar depois é retrabalho.

### Fase 1 antes de tudo: protótipo em PowerShell

Antes de escrever uma linha de C#, o protótipo é um `.ps1` chamado por um `.bat`. Objetivo: validar em uma semana **quais campos realmente importam comercialmente** e quais fontes de dados funcionam no parque real.

Isso evita construir UI e modelo de dados em cima de premissas erradas. O `.ps1` da Fase 1 é descartável — não é base de código, é instrumento de descoberta. O JSON que ele produz, no entanto, já deve seguir o schema da seção 5.

---

## 2. Estrutura da solução

```
epicora-checkup/
├─ src/
│  ├─ EpicoraCheckup.App/            WinForms: telas, wizard, binding
│  ├─ EpicoraCheckup.Core/           Modelo de dados, contratos, orquestrador
│  ├─ EpicoraCheckup.Collectors/     Um coletor por domínio
│  ├─ EpicoraCheckup.Rules/          Motor de regras + regras declarativas
│  ├─ EpicoraCheckup.Optimizers/     Fase 5. Um otimizador por ação
│  ├─ EpicoraCheckup.Reporting/      JSON, HTML, log
│  └─ EpicoraCheckup.Consolidator/   Ferramenta separada, roda no escritório
├─ tests/
│  ├─ EpicoraCheckup.Rules.Tests/    Testes de regra com JSON sintético
│  └─ fixtures/                      JSONs de máquinas reais anonimizados
├─ docs/                             Estes três documentos
├─ rules/                            Regras em JSON, versionadas
└─ .github/workflows/build.yml       CI: build, teste, assinatura, release
```

**Regra de dependência:** `Collectors`, `Rules`, `Optimizers` e `Reporting` dependem de `Core`. Nenhum deles depende de `App`. Nenhum deles referencia WinForms. Isso é o que permite testar tudo sem UI e reaproveitar no consolidador.

---

## 3. Arquitetura

### 3.1 Contrato de coletor

Cada domínio de coleta implementa uma interface única. Esboço conceitual:

```csharp
public interface ICollector
{
    string Id { get; }                  // "storage", "security", ...
    string DisplayName { get; }         // rótulo na tela 2
    bool RequiresElevation { get; }     // define se será marcado como Ignorado
    int EstimatedSeconds { get; }        // só para a barra de progresso

    CollectorResult Collect(CollectionContext ctx, CancellationToken ct);
}
```

`CollectorResult` carrega: **estado** (Concluído / Ignorado / Falhou), o payload de dados, um resumo de uma linha para a tela, e a lista de erros não fatais.

### 3.2 Orquestrador

Roda os coletores em sequência e:

- Envolve **cada** coletor em try/catch individual. Falha de um nunca aborta os outros. Este é o requisito de robustez número um.
- Impõe **timeout por coletor** (sugestão: 20 segundos). WMI pode travar indefinidamente em máquina com repositório corrompido, o que é justamente o tipo de máquina que a Epicora vai encontrar. Sem timeout, a ferramenta fica pendurada na frente do cliente.
- Reporta progresso para a UI.

**Confiança M sobre o timeout:** cancelar uma chamada WMI síncrona em andamento não é trivial em .NET. Provável necessidade de rodar o coletor em thread separada com `Task` + timeout e aceitar que a thread pode ficar órfã. Prototipar isso cedo, na Fase 2, porque afeta a estrutura do orquestrador.

### 3.3 UI e threading

**Requisito:** a janela nunca congela.

Coleta roda **fora da thread da UI**. Dois caminhos válidos em WinForms: `BackgroundWorker` (relato de progresso embutido) ou `async/await` com `IProgress<T>`. Ambos existem e são bem documentados — **confira a sintaxe atual na documentação, não em memória de ninguém.**

Toda atualização de controle da UI a partir da thread de trabalho precisa ser marshalizada para a thread da UI. Ignorar isso produz exceção intermitente e difícil de reproduzir.

### 3.4 Elevação

`app.manifest` com `requestedExecutionLevel` como `requireAdministrator`. **Confiança A** de que o mecanismo existe; confirmar o valor exato do atributo na doc.

Mas o requisito funcional é mais forte que isso: **a ferramenta deve rodar sem elevação também.** Cenário real: técnico sem a senha de administrador local na máquina do cliente. Nesse caso, os coletores com `RequiresElevation = true` são marcados como **Ignorado — sem privilégio**, e o relatório sai parcial e honesto.

Implementação: detectar elevação no início e passar o resultado no `CollectionContext`.

### 3.5 Motor de regras

**Declarativo, não imperativo.** As regras vivem em JSON em `rules/`, não espalhadas como `if` pelo código. Motivo: as regras vão mudar com frequência a partir de aprendizado de campo, e cada mudança não pode exigir recompilar e redistribuir.

Formato completo e o conjunto inicial de regras estão em `03-matriz-riscos-otimizacoes.md`.

O motor avalia cada regra contra o JSON de coleta e produz três estados: **conforme**, **não conforme**, **indeterminado**. Nunca inferir "não conforme" a partir de dado ausente.

### 3.6 Otimizadores (Fase 5)

Contrato análogo ao coletor, com quatro exigências extras:

```csharp
public interface IOptimizer
{
    string Id { get; }
    Severity Impact { get; }
    bool IsIrreversible { get; }
    bool RequiresUserConsent { get; }

    Measurement MeasureBefore(...);      // obrigatório
    OptimizerResult Apply(...);          // registra valor original no log
    Measurement MeasureAfter(...);       // obrigatório
}
```

Nenhum otimizador pode ser executado sem `MeasureBefore` gravado. Isso é imposto pelo orquestrador, não confiado ao autor do otimizador.

---

## 4. Fontes de dados

Namespace padrão: `root\CIMV2`, salvo indicado. Coluna **Elev.** indica necessidade de privilégio administrativo.

### 4.1 Identificação

| Dado | Fonte | Conf. | Elev. |
|---|---|---|---|
| Hostname, domínio, fabricante, modelo, RAM total | `Win32_ComputerSystem` | A | Não |
| UUID, número de série do produto | `Win32_ComputerSystemProduct` | A | Não |
| Serial / service tag, versão e data do BIOS | `Win32_BIOS` | A | Não |
| Placa-mãe: produto, fabricante, serial | `Win32_BaseBoard` | A | Não |
| Tipo de chassi (desktop / notebook / all-in-one) | `Win32_SystemEnclosure`, propriedade `ChassisTypes` | M | Não |

`ChassisTypes` é um array de códigos numéricos. A tabela de mapeamento código→tipo precisa ser conferida na doc, e vários fabricantes preenchem errado. **Recomendação:** usar presença de bateria (`Win32_Battery`) como confirmação secundária de notebook.

### 4.2 Processador e memória

| Dado | Fonte | Conf. | Elev. |
|---|---|---|---|
| CPU: modelo, núcleos físicos, threads, clock máximo | `Win32_Processor` | A | Não |
| Virtualização habilitada no firmware | `Win32_Processor.VirtualizationFirmwareEnabled` | M | Não |
| Pentes de RAM: capacidade, velocidade, banco, part number | `Win32_PhysicalMemory` | A | Não |
| **Total de slots físicos** | `Win32_PhysicalMemoryArray.MemoryDevices` | M | Não |
| Capacidade máxima suportada | `Win32_PhysicalMemoryArray.MaxCapacity` | M | Não |

**Slots livres = total de slots − pentes instalados.** Este é um dos dados mais valiosos comercialmente: permite orçar upgrade de RAM na hora, sem abrir a máquina.

`MaxCapacity` é notoriamente mal preenchido por vários fabricantes de placa-mãe. Tratar como referência, não como verdade; se vier zero ou absurdo, marcar indeterminado.

Tipo de memória (DDR3/DDR4/DDR5): existem duas propriedades, `MemoryType` (legada, frequentemente retorna 0 ou valor errado) e `SMBIOSMemoryType`. **Confiança M** sobre qual usar e sobre a tabela de códigos. Conferir na doc e testar em máquina DDR4 e DDR5 real.

### 4.3 Armazenamento — a categoria mais importante

| Dado | Fonte | Conf. | Elev. |
|---|---|---|---|
| Volumes: letra, tamanho, espaço livre, sistema de arquivos | `Win32_LogicalDisk` | A | Não |
| Discos físicos: modelo, tamanho, interface, serial | `Win32_DiskDrive` | A | Não |
| **Tipo de mídia (HDD / SSD)** e barramento (SATA / NVMe) | `MSFT_PhysicalDisk` em `root\Microsoft\Windows\Storage`, propriedades `MediaType` e `BusType` | M | Provável |
| Status básico de saúde | `MSFT_PhysicalDisk.HealthStatus` | M | Provável |
| Predição de falha SMART (booleano) | `MSStorageDriver_FailurePredictStatus` em `root\wmi` | M | Sim |
| SMART detalhado: horas ligado, setores realocados, desgaste de SSD | `smartctl` (smartmontools) embutido | B | Sim |

**Alertas importantes:**

- **Não use `Win32_DiskDrive.MediaType` para distinguir SSD de HDD.** Essa propriedade retorna valores genéricos como "Fixed hard disk media" independentemente da tecnologia. É uma armadilha clássica.
- O namespace `root\Microsoft\Windows\Storage` é o caminho correto para tipo de mídia, mas está marcado **M**: nomes de classe, propriedades e valores enumerados devem ser conferidos na doc. Em PowerShell o equivalente é `Get-PhysicalDisk`, útil para prototipar na Fase 1 e descobrir os valores reais.
- **SMART detalhado é a parte difícil do projeto.** Não afirmo que existe caminho puro em WMI para horas ligado, contagem de setores realocados e percentual de desgaste de SSD. O caminho realista é embutir `smartctl` como recurso, extrair em pasta temporária e parsear a saída JSON. Isso traz custo: tamanho do binário, licença (smartmontools é GPL — **verificar as implicações de distribuir junto com software proprietário antes de decidir**), e mais um executável desconhecido para o antivírus reclamar. **Decisão a tomar na Fase 0.**

### 4.4 Sistema operacional e licenciamento

| Dado | Fonte | Conf. | Elev. |
|---|---|---|---|
| Edição, versão, build, arquitetura, data de instalação, último boot | `Win32_OperatingSystem` | A | Não |
| Build revision (UBR) e DisplayVersion (ex.: 22H2) | Registro: `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion` | A | Não |
| Status de ativação | `SoftwareLicensingProduct`, propriedade `LicenseStatus` | M | Provável |
| Atualizações instaladas | `Win32_QuickFixEngineering` | A | Não |

**Cuidado com `Win32_QuickFixEngineering`:** ela não lista tudo. Atualizações cumulativas modernas e updates entregues por outros mecanismos frequentemente não aparecem. Usar `Win32_OperatingSystem` para a build atual e comparar com a build mais recente conhecida é mais confiável para responder "esta máquina está atualizada?" — mas isso exige manter uma tabela de builds atualizada no repositório, o que é manutenção recorrente. **Ponto aberto para decisão.**

`SoftwareLicensingProduct` pode retornar muitos registros e é lenta. Filtrar pela consulta, não em memória.

### 4.5 Compatibilidade com Windows 11

Categoria de alto valor comercial: sozinha justifica um relatório.

| Dado | Fonte | Conf. | Elev. |
|---|---|---|---|
| TPM: presença, versão, estado | `Win32_Tpm` em `root\CIMV2\Security\MicrosoftTpm` | M | Sim |
| Secure Boot habilitado | Registro: `HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State`, valor `UEFISecureBootEnabled` | M | Provável |
| UEFI vs BIOS legado | Ver nota abaixo | B | — |
| CPU na lista de suportados pela Microsoft | Tabela local no repositório | — | Não |

**Firmware UEFI vs Legacy:** não existe propriedade WMI direta e confiável para isso, tanto quanto eu sei. Caminhos possíveis: variável de ambiente `firmware_type` (disponível a partir do Windows 8, **confiança M**), API `GetFirmwareEnvironmentVariable` via P/Invoke, ou inferir pelo estilo de partição do disco de sistema (GPT sugere UEFI). **Prototipar na Fase 1 e escolher.**

**Lista de CPUs suportadas para Windows 11:** a Microsoft publica listas por fabricante. Precisa ser embutida como recurso e atualizada periodicamente. Sem ela, o veredito de compatibilidade fica incompleto. Considerar tratar CPU como "verificar manualmente" na v1 em vez de arriscar afirmação errada.

Em PowerShell, `Get-Tpm` e `Confirm-SecureBootUEFI` existem e são úteis para o protótipo, mas ambos exigem elevação e `Confirm-SecureBootUEFI` lança exceção em máquina com BIOS legado em vez de retornar falso. Tratar a exceção, não deixar propagar.

### 4.6 Segurança

| Dado | Fonte | Conf. | Elev. |
|---|---|---|---|
| Antivírus: nome, estado, atualização | `AntiVirusProduct` em `root\SecurityCenter2` | A (classe) / **B (interpretação)** | Provável |
| Estado do Defender | `MSFT_MpComputerStatus` em `root\Microsoft\Windows\Defender` | M | Provável |
| BitLocker por volume | `Win32_EncryptableVolume` em `root\CIMV2\Security\MicrosoftVolumeEncryption` | M | Sim |
| Firewall por perfil | `MSFT_NetFirewallProfile` em `root\StandardCimv2` | M | Provável |
| RDP habilitado | Registro: `HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server`, valor `fDenyTSConnections` | M | Não |
| SMBv1 ativo | Estado da feature opcional `SMB1Protocol` | M | Sim |
| Usuário do dia a dia é admin local | `Win32_GroupUser` cruzado com o grupo Administradores | M | Provável |

**O ponto mais delicado de toda a ferramenta: a propriedade `productState` de `AntiVirusProduct`.** É um inteiro cujo significado é uma máscara de bits **não documentada oficialmente pela Microsoft**. Todas as interpretações que circulam são de engenharia reversa da comunidade.

Consequência prática, e isso é um requisito, não uma sugestão: **se a interpretação do estado do antivírus não for inequívoca, o resultado é "indeterminado", nunca "antivírus desatualizado".** Um falso positivo aqui é o erro mais caro possível — dizer a um cliente que ele está sem proteção quando não está queima a reunião e a credibilidade da Epicora.

Recomendação adicional: `root\SecurityCenter2` não existe em edições Server. Se servidor entrar no escopo, caminho diferente.

**Grupo de administradores:** o nome do grupo é localizado ("Administradores" em português). Nunca comparar por nome. Usar o SID conhecido do grupo de administradores locais. **Confiança M** sobre o valor exato do SID — conferir na doc.

### 4.7 Software instalado

| Dado | Fonte | Conf. | Elev. |
|---|---|---|---|
| Programas instalados | Registro, três chaves `Uninstall` | A | Não |

Chaves a ler:
- `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`
- `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall`
- `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`

Campos: `DisplayName`, `DisplayVersion`, `Publisher`, `InstallDate`, `EstimatedSize`. Filtrar entradas sem `DisplayName` e com `SystemComponent = 1`.

**Proibido usar `Win32_Product`.** Além de ser lenta, ela dispara reconfiguração de pacotes MSI na máquina consultada. Isso pode gerar efeitos colaterais reais na máquina do cliente. Esta é uma proibição, não uma preferência de performance.

Classificação a fazer sobre a lista: ferramentas de acesso remoto presentes, antivírus de terceiros, agentes de EDR e backup, indícios de software sem licença (versões de Office, AutoCAD, Adobe fora de padrão corporativo). A classificação por si é o que gera achado comercial.

### 4.8 Inicialização

| Dado | Fonte | Conf. | Elev. |
|---|---|---|---|
| Itens de inicialização | `Win32_StartupCommand` | A (existe) / M (cobertura) | Não |
| Chaves Run diretas | `HKLM` e `HKCU` → `SOFTWARE\Microsoft\Windows\CurrentVersion\Run` e `RunOnce` | A | Não |
| Estado habilitado/desabilitado (o que o Gerenciador de Tarefas mostra) | `...\Explorer\StartupApproved\Run` | B | Não |
| Pastas de Inicialização | `shell:startup` e `shell:common startup` | A | Não |
| Tarefas agendadas com gatilho de logon | API do Task Scheduler | M | Provável |

`Win32_StartupCommand` **não cobre tudo** — notadamente tarefas agendadas e alguns mecanismos modernos de inicialização. Para inventário é aceitável; para a otimização da Fase 5 é insuficiente e precisa ser complementado.

A chave `StartupApproved` armazena o estado habilitado/desabilitado em formato **binário não documentado**. Está marcada **B**. Se a Fase 5 for desativar itens de inicialização, este é o ponto que precisa de prototipagem cuidadosa: escrever formato binário errado no registro pode corromper o estado de inicialização da máquina. **Alternativa mais segura a considerar: mover a entrada da chave `Run` para uma chave de backup própria da Epicora, o que é totalmente reversível e documentado por nós mesmos.**

### 4.9 Rede

| Dado | Fonte | Conf. | Elev. |
|---|---|---|---|
| Adaptadores, MAC, estado, velocidade de link | `Win32_NetworkAdapter` | A | Não |
| IP, máscara, gateway, DNS, DHCP | `Win32_NetworkAdapterConfiguration` | A | Não |
| Cabo vs Wi-Fi | `MSFT_NetAdapter` em `root\StandardCimv2` | M | Não |

Filtrar adaptadores virtuais (VPN, Hyper-V, VirtualBox, loopback) da apresentação principal, mas **registrá-los no JSON** — a presença de adaptador de VPN é informação relevante.

Velocidade de link negociada é achado útil: adaptador gigabit negociando 100 Mbps normalmente indica cabo ou switch ruim, e isso é gancho direto para a vertical de infraestrutura de rede.

### 4.10 Bateria (notebooks)

| Dado | Fonte | Conf. | Elev. |
|---|---|---|---|
| Presença, carga atual, estado | `Win32_Battery` | A | Não |
| Capacidade de projeto vs. capacidade atual | `powercfg /batteryreport` | M | Provável |

**`Win32_Battery.DesignCapacity` retorna nulo na maioria dos notebooks.** Não confiar. O caminho realista é executar `powercfg /batteryreport /output <temp>` e parsear o HTML gerado, ou o XML se disponível. Marcado **M** porque não afirmo o formato exato da saída atual nem se há opção de XML — prototipar.

Desgaste de bateria acima de 30% é achado de venda direta: substituição de bateria é serviço de ticket baixo e alta percepção de valor.

### 4.11 Estabilidade

| Dado | Fonte | Conf. | Elev. |
|---|---|---|---|
| Desligamentos inesperados, erros de disco, erros críticos | Event Log: canais System e Application | A | Sim (canal Security exige elevação; System geralmente não) |

Janela sugerida: 30 dias. Filtrar por IDs de evento específicos, **não** ler o log inteiro — ler tudo é lento e infla o JSON.

Os IDs de evento relevantes (desligamento inesperado, erro de disco, falha de serviço crítico) estão marcados **B** neste documento: **não vou listar números de ID que não posso confirmar.** Levantar na doc da Microsoft na Fase 1 e registrar a lista em `rules/event-ids.json`.

**Não coletar** o canal Security e **não coletar** eventos de logon de usuário. Está fora do escopo de privacidade definido no documento funcional.

### 4.12 Temperatura

Não coletar e não prometer no relatório. `MSAcpi_ThermalZoneTemperature` existe, mas raramente é implementada corretamente em hardware de consumo, e quando responde frequentemente devolve a temperatura de uma zona ACPI irrelevante, não do núcleo da CPU. Prometer temperatura de CPU no relatório e entregar número errado é pior que omitir.

---

## 5. Schema do JSON

Fonte única de verdade. Versionado — o consolidador precisa lidar com JSONs de versões diferentes.

```jsonc
{
  "schemaVersion": "1.0",
  "tool": { "name": "EpicoraCheckup", "version": "1.0.3", "commit": "a1b2c3d" },
  "execution": {
    "startedAt": "2026-07-29T14:03:11-03:00",
    "finishedAt": "2026-07-29T14:04:28-03:00",
    "durationSeconds": 77,
    "elevated": true,
    "technician": "Nome do técnico",
    "diagnosticId": "DIAG-2026-014"
  },
  "client": { "name": "Cliente X", "unit": "Matriz" },
  "manual": {
    "machineLabel": "ADM-04",
    "responsible": "Nome do usuário",
    "department": "Administrativo",
    "physicalLocation": "Sala 2, mesa 3",
    "assetTag": "PAT-00291",
    "physicalCondition": "Ventoinha ruidosa, teclado desgastado",
    "notes": "Usuário relata lentidão ao abrir planilhas"
  },
  "collectors": [
    {
      "id": "storage",
      "status": "Completed",       // Completed | Skipped | Failed
      "skipReason": null,
      "durationMs": 4120,
      "summary": "Disco de sistema: HDD 500 GB, 6% livre",
      "errors": [],
      "data": { }                  // payload específico do coletor
    }
  ],
  "findings": [
    {
      "ruleId": "STO-001",
      "severity": "Critical",
      "state": "NonCompliant",     // Compliant | NonCompliant | Indeterminate
      "title": "Disco de sistema é HDD",
      "clientText": "...",
      "recommendedAction": "...",
      "evidence": { "diskModel": "...", "mediaType": "HDD" },
      "markedFalsePositive": false,
      "falsePositiveJustification": null
    }
  ],
  "score": { "value": 34, "band": "Red", "verdict": "Replace" },
  "optimization": {
    "executed": true,
    "restorePointCreated": true,
    "restorePointId": "...",
    "actions": [
      {
        "id": "OPT-TEMP",
        "authorizedBy": "Nome do técnico",
        "userConsent": true,
        "measureBefore": { "freeSpaceBytes": 8123456789 },
        "result": "Success",
        "measureAfter": { "freeSpaceBytes": 22456789012 },
        "gain": { "freedBytes": 14333332223 },
        "originalValues": { },
        "errors": []
      }
    ]
  }
}
```

Regras de serialização:

- Datas em ISO 8601 **com offset de fuso**. Nunca hora local sem offset.
- Tamanhos sempre em **bytes**, inteiros. Formatação para GB é responsabilidade da camada de apresentação.
- Campo ausente = `null`, com o motivo registrado no coletor correspondente. Nunca zero, nunca string vazia, nunca `"N/A"` — isso destrói a análise no consolidador.
- Nome do arquivo: `HOSTNAME_SERIAL_AAAAMMDD.json`, com sanitização de caracteres inválidos. Serial pode vir vazio ou com espaços em muitos fabricantes; ter fallback determinístico.

---

## 6. Relatório HTML

Arquivo único, autocontido: CSS inline, sem CDN, sem JavaScript externo, sem fonte remota. Precisa abrir em máquina sem internet e continuar legível daqui a cinco anos.

Estrutura: cabeçalho de identificação → score e veredito → riscos por severidade → bloco "não foi possível verificar" → inventário detalhado → resultado da otimização, se houve → rodapé com versão da ferramenta e timestamp.

Impressão em A4 precisa funcionar (`@media print`). Técnico em campo às vezes precisa entregar em papel.

O relatório executivo do parque, na identidade visual da Epicora, é gerado pelo consolidador em Markdown e convertido via a skill de documento de marca já existente.

---

## 7. Consolidador

Projeto separado, roda no escritório. Entrada: pasta com N arquivos JSON. Saída: Markdown do relatório executivo + XLSX de inventário.

Precisa: tolerar JSONs de `schemaVersion` diferentes, tolerar JSON corrompido (relatar e seguir), deduplicar por UUID quando a mesma máquina foi escaneada duas vezes na mesma visita (manter a mais recente).

Análises: distribuição de vereditos, ranking de riscos mais frequentes, lista de máquinas incompatíveis com Windows 11, custo estimado de remediação por faixa, comparativo entre setores.

---

## 8. Distribuição, atualização e assinatura

**Esta é a seção com o maior risco operacional do projeto.**

### 8.1 Por que não é `git clone`

`git clone` exige Git instalado na máquina do cliente, que na maioria dos casos não está. Descartado.

O mecanismo correto é **GitHub Releases**. O padrão de URL estável, confirmado na documentação do GitHub, é:

```
https://github.com/<owner>/<repo>/releases/latest/download/<nome-do-asset>
```

Esse link resolve sempre para o asset mais recente, sem precisar mudar a URL a cada versão. Requisitos: o asset precisa ter **nome fixo entre releases** (`EpicoraCheckup.exe`, sem número de versão no nome) e ser anexado manualmente ou pelo CI ao release.

### 8.2 O problema do repositório privado — decisão da direção necessária

O link acima funciona diretamente **apenas em repositório público**. Para repositório privado, o download exige um Personal Access Token no cabeçalho da requisição.

Isso cria um trilema, e nenhuma opção é indolor:

| Opção | Vantagem | Custo real |
|---|---|---|
| **A. Repositório público** | Download trivial, uma URL, funciona em qualquer máquina | Código-fonte visível para concorrentes e clientes. Regras de risco e textos comerciais expostos. |
| **B. Repositório privado + PAT** | Código protegido | Token precisa estar em algum lugar acessível ao técnico. Token embutido no launcher **é token vazado** — não faça isso. Token digitado pelo técnico é atrito em cada máquina. |
| **C. Fonte privado + distribuição separada** | Código protegido e download trivial | Mais uma peça de infraestrutura para manter |

**Recomendação: opção C.** Repositório de código privado no GitHub, e o CI publica o binário em um bucket S3 com CloudFront (a Epicora já opera AWS), atrás de uma URL curta própria. Custo mensal desprezível para arquivo de poucos MB, e a URL fica sob controle da Epicora — o que também permite revogar acesso e trocar de host sem mudar o procedimento do técnico.

Se a direção preferir simplicidade máxima e não considerar o código um ativo a proteger, a opção A é legítima. **É decisão de negócio, não técnica, e precisa estar resolvida antes da Fase 3.**

### 8.3 Verificação de versão pela própria ferramenta

Na inicialização, a ferramenta consulta a versão mais recente publicada e compara com a própria. Se estiver desatualizada, exibe aviso não bloqueante com o link.

- Se o download vier do S3/CloudFront (opção C): um arquivo `latest.json` estático ao lado do binário. Simples e sem limite de requisição.
- Se vier do GitHub público (opção A): a API de releases resolve, mas há **limite de requisições para chamadas não autenticadas** — em torno de 60 por hora por IP, valor aproximado e que **deve ser confirmado na documentação atual do GitHub**. Para o volume da Epicora provavelmente é suficiente, mas vários técnicos atrás do mesmo IP de cliente podem esbarrar nisso.

Requisito: **falha na verificação nunca bloqueia a execução.** Timeout curto (3 segundos), erro silencioso, segue em frente.

### 8.4 Antivírus, EDR e SmartScreen — planejar, não esperar

Executável desconhecido, baixado da internet, executado com elevação, varrendo hardware, lendo o registro e — na Fase 5 — apagando arquivos e mexendo em serviços. Esse é literalmente o perfil comportamental que um EDR moderno bloqueia. **Vai acontecer.** Não é hipótese.

Além disso, arquivo baixado por navegador recebe marca de zona (Mark-of-the-Web), o que aciona o SmartScreen com o aviso de aplicativo não reconhecido.

Mitigações, em ordem de eficácia:

1. **Certificado de assinatura de código.** Certificado OV comum reduz o problema mas precisa acumular reputação, o que leva tempo e volume de downloads. Certificado **EV** dá reputação praticamente imediata no SmartScreen, mas custa mais e hoje exige armazenamento em token físico ou HSM, o que complica assinatura automatizada no CI. **Levantar preço e requisitos atuais com uma autoridade certificadora na Fase 0** — não estimo valores aqui porque preço e regras de emissão mudam com frequência e eu não tenho fonte verificada e atual para citar.
2. Assinatura no CI a cada release, com hash SHA-256 publicado ao lado do binário.
3. **Procedimento documentado de exceção** para o técnico: como pedir ao responsável de TI do cliente uma exclusão temporária, e como proceder se ele negar.
4. **Plano de contingência obrigatório:** se o executável for bloqueado e não houver como liberar, o técnico precisa de um caminho alternativo. Provavelmente o script PowerShell da Fase 1, mantido vivo justamente para isso. Isso muda a decisão sobre o protótipo: **ele não é descartável, é o fallback permanente.** Vale registrar isso na Fase 0.

### 8.5 CI/CD

Workflow no GitHub Actions: build → testes de regra → assinatura → publicação do asset + `latest.json` + `SHA256SUMS`.

Versionamento semântico. Tag `v*` dispara o release. Número de versão gravado no JSON de saída — sem isso é impossível auditar qual versão produziu qual relatório, e isso vai importar no primeiro relatório contestado por um cliente.

---

## 9. Log e tratamento de erro

Log em texto, mesmo diretório da saída, um arquivo por execução. Níveis: DEBUG, INFO, WARN, ERROR.

Obrigatoriamente registrados: início e fim de cada coletor com duração; toda exceção com stack trace; **toda ação de otimização com valor original antes da alteração**; resultado da verificação de versão.

**Nunca no log:** nome de arquivo de usuário, conteúdo de arquivo, credencial, chave de produto, dado pessoal identificável além do que já está no bloco manual autorizado.

O log vai junto no pacote de entrega interna, não para o cliente.

---

## 10. Testes

**Unitários — motor de regras.** Alimentar o motor com JSONs sintéticos e verificar o achado esperado. Cobertura obrigatória de 100% das regras da matriz, incluindo o caso `Indeterminate` de cada uma. É a parte mais testável e a que mais importa: regra errada gera relatório errado gera reunião perdida.

**Integração — coletores.** Rodar em VMs: Windows 10 22H2, Windows 11 23H2 ou mais recente, máquina em domínio, máquina sem TPM, máquina com HDD.

**Campo — Fase 1 e 2.** Mínimo dez máquinas reais antes de qualquer uso comercial, incluindo pelo menos uma com EDR de terceiro e uma sem privilégio administrativo disponível.

**Fase 5 — otimização.** Exclusivamente em VM com snapshot, depois em todas as máquinas internas da Epicora, depois cliente. Cada otimizador precisa de teste de reversão comprovada antes de entrar em release.

Manter em `tests/fixtures/` os JSONs anonimizados de máquinas reais. Esse acervo é o ativo de teste mais valioso do projeto e cresce a cada diagnóstico feito.

---

## 11. Pontos abertos — decisões da Fase 0

Nenhum destes deve ser resolvido dentro do código por decisão individual de quem implementa.

1. **Alvo .NET Framework 4.8 ou 4.7.2** — depende de servidores entrarem ou não no escopo.
2. **Repositório público, privado ou distribuição separada** (seção 8.2) — decisão de negócio.
3. **Certificado de assinatura de código: OV ou EV, ou nenhum na v1** — depende de orçamento e do resultado do levantamento de preço.
4. **Embutir `smartctl`?** Depende do valor comercial do SMART detalhado versus custo de tamanho, licença GPL e atrito com antivírus.
5. **Manter tabela de builds do Windows no repositório** para avaliar se a máquina está atualizada? É manutenção recorrente. Se não, o achado fica menos preciso.
6. **Embutir a lista de CPUs suportadas para Windows 11?** Se não, o veredito de compatibilidade fica parcial e precisa ser declarado como tal.
7. **Mecanismo de desativação de item de inicialização** (seção 4.8): escrever em `StartupApproved` (formato binário não documentado, risco) ou mover a entrada para chave de backup própria (mais seguro, recomendado).
8. **Idioma da interface e do relatório** — português como padrão; considerar se algum cliente vai precisar de inglês.
9. **Confirmar que o protótipo PowerShell é fallback permanente**, não descartável (seção 8.4).

---

## 12. Resumo das incertezas que quero deixar explícitas

Tenho **alta confiança** nas classes WMI clássicas de `root\CIMV2` (`Win32_ComputerSystem`, `Win32_BIOS`, `Win32_Processor`, `Win32_PhysicalMemory`, `Win32_OperatingSystem`, `Win32_LogicalDisk`, `Win32_DiskDrive`, `Win32_NetworkAdapter*`, `Win32_QuickFixEngineering`), nas chaves `Uninstall` do registro, e no padrão de URL de release do GitHub e na presença do .NET Framework 4.8 no Windows — estes dois últimos verifiquei na documentação oficial ao escrever este documento.

Tenho **confiança média** nos caminhos exatos de TPM, BitLocker, Secure Boot, firewall, storage moderno e ativação de licença. Confira cada um na documentação atual da Microsoft antes de codificar.

Tenho **baixa confiança** e recomendo prototipagem antes de estimar prazo: SMART detalhado, interpretação de `productState` do antivírus, formato binário de `StartupApproved`, detecção confiável de UEFI vs Legacy, IDs de evento específicos, e formato de saída do `powercfg /batteryreport`.

Não citei preço de certificado, número de ID de evento ou limite exato de API porque não tenho fonte verificada e atual para nenhum deles. Estão marcados para levantamento.
