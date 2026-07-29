# Mapeamento campo → decisão comercial

Entregável da Fase 0: *"cada campo mapeado à decisão comercial que sustenta"* (doc funcional §11).

**Critério de corte:** campo que não alimenta nenhuma regra nem nenhuma seção do relatório sai do schema. Campo que sobrevive só por "pode ser útil um dia" é peso morto no JSON, no coletor, no consolidador e na revisão.

Coluna **Conf.** repete o nível de confiança da fonte no doc técnico §4 — A alta, M média, B baixa. **Todo campo M ou B nasce com as regras que dependem dele desabilitadas**, até validação em campo na Fase 1.

---

## Os 16 coletores

O documento funcional §5 lista 15 etapas na tela 2, com "Processador e memória" como uma. Aqui estão separados em `cpu` e `memory`: são domínios com falha independente, e o timeout por coletor não deve derrubar os dois quando só um travar. A tela 2 passa a mostrar 16 etapas.

| Id | Etapa na tela 2 | Elev. | Regras que consome |
|---|---|---|---|
| `machine` | Identificação da máquina | Não | HW-003, HW-004, HW-005 |
| `cpu` | Processador | Não | CPU-001, CPU-002, CPU-003 |
| `memory` | Memória | Não | MEM-001..005 |
| `storage` | Armazenamento e saúde de disco | Parcial | STO-001..008 |
| `devices` | Placa de vídeo e dispositivos | Não | (inventário; HW-006 proposta) |
| `os` | Sistema operacional e licenciamento | Parcial | OS-001..007 |
| `updates` | Atualizações do Windows | Não | SEC-010 |
| `win11` | Compatibilidade com Windows 11 | Sim | W11-001..006 |
| `security` | Segurança e criptografia | Sim | SEC-004..006, SEC-008, SEC-009 |
| `antivirus` | Antivírus | Provável | SEC-001..003, SW-005 |
| `software` | Software instalado | Não | SW-003..007, SEC-011, SEC-012 |
| `startup` | Programas de inicialização | Não | SW-001, SW-002 |
| `network` | Rede | Não | NET-001..004 |
| `accounts` | Contas e privilégios | Provável | SEC-007 |
| `battery` | Bateria | Provável | HW-001, HW-002 |
| `events` | Eventos críticos | Não | EST-001..003 |

---

## Campos que sustentam decisão comercial direta

Estes são a razão de a ferramenta existir. Cada um responde a uma pergunta que o cliente faz ou que a proposta precisa responder.

| Campo | Conf. | Regra | Decisão comercial que sustenta |
|---|---|---|---|
| `storage.systemDisk.mediaType` | **M** | STO-001 | **Venda de SSD.** A maior causa isolada de lentidão em máquina com CPU e RAM adequados. Um achado, um orçamento |
| `memory.freeSlots` | **M** | MEM-003 / MEM-004 | **Upgrade de RAM vs. substituir a máquina.** É a mesma condição de RAM com desfecho oposto: slot livre = pente adicional barato; sem slot = trocar tudo. Permite orçar na hora, sem abrir a máquina |
| `win11.eligible` | **M** | W11-006 | **A frase do relatório executivo:** "18 das 50 máquinas não migram para Windows 11". Sozinha justifica um diagnóstico de parque |
| `win11.tpm.enabled` | **M** | W11-003 | **Serviço vs. hardware.** TPM 2.0 desativado no firmware resolve com uma visita e cinco minutos na BIOS. TPM ausente é máquina nova. Errar isso custa caro nos dois sentidos |
| `storage.systemVolume.freePercent` | A | STO-002, STO-003 | **Entrada para servidor de arquivos ou aumento de capacidade** — e é o achado que a otimização da Fase 5 resolve no mesmo dia, provando competência |
| `storage.systemDisk.failurePredicted` | **M** | STO-004 | **Urgência.** Backup imediato + troca de disco, com prioridade sobre tudo. Ticket de alta percepção de valor |
| `events.diskErrors` | **B** | EST-002 | Mesma decisão, por caminho independente. **A coincidência dos dois é o sinal mais forte possível de disco morrendo** |
| `os.productFamily` | A | OS-001, OS-002 | **Migração para Windows 11** ou substituição. Windows 10 sem suporte desde 14/10/2025 é o gancho de segurança mais direto que existe |
| `network.linkDowngraded` | **M** | NET-001 | **Vertical de infraestrutura de rede.** Um achado por máquina que, repetido no parque, vira diagnóstico de cabeamento |
| `battery.wearPercent` | **M** | HW-001, HW-002 | **Troca de bateria.** Serviço de ticket baixo e alta percepção de valor |
| `software.classification.backupAgents` | A | SEC-012 | **Venda de rotina de backup** — o item de maior valor recorrente da vertical de TI |
| `accounts.currentUser.isLocalAdmin` | **M** | SEC-007 | **Projeto de hardening.** Medida de maior efeito e menor custo em estação de trabalho |
| `security.smb1.enabled` | **M** | SEC-008 | **Projeto de segurança de rede.** Não é clique — exige validar que nenhuma impressora ou equipamento industrial antigo depende dele |
| `machine.approxAgeYears` | A | HW-004, HW-005 | **Plano de renovação de parque.** Aproximação declarada, nunca idade exata |

---

## Campos de contexto — não geram achado sozinhos, mas mudam o achado

Cortar qualquer um destes produz falso positivo. É por isso que estão no schema.

| Campo | Por que existe |
|---|---|
| `machine.isLaptop` | Separa SEC-004 (notebook sem BitLocker, Alto) de SEC-005 (desktop, Médio). Notebook sai da empresa; desktop não |
| `machine.domainJoined` | Habilita OS-004 e NET-004. Sem contexto de ambiente corporativo, as duas viram ruído |
| `manual.corporateEnvironment` | Mesmo papel, quando a máquina não está em domínio mas o ambiente é corporativo. Marcação do técnico |
| `antivirus.securitySoftwareInInventory` | **Cruzamento obrigatório.** Impede o pior falso positivo possível: dizer "sem antivírus" para quem tem EDR corporativo |
| `antivirus.products[].interpretation.confidence` | Se não for `High`, SEC-001/002/003 resolvem `Indeterminate`. É o mecanismo que torna o requisito vinculante do doc 03 §4.6 verificável no dado, não só na intenção |
| `storage.systemDisk.mediaType` = `Unknown` | Bloqueia OPT-DEFRAG na Fase 5. Este é o caso em que "não sei" **tem que impedir** a ação, não liberá-la |
| `accounts.administratorsGroupResolvedBySid` | Se false, o grupo foi resolvido por nome — e nome de grupo é localizado. SEC-007 resolve `Indeterminate` |
| `os.buildFreshness.evaluated` | Se false (tabela de builds vencida), OS-005 resolve `Indeterminate` em vez de acusar máquina atualizada de estar desatualizada — ADR-005 |
| `cpu.win11SupportBasis` | Distingue "CPU não suportada" de "não consegui avaliar". ADR-006 |
| `updates.coverageIsPartial` | Sempre `true`. Lembrete estrutural de que `Win32_QuickFixEngineering` não lista tudo e nenhuma regra pode concluir "desatualizado" só a partir dela |
| `startup.items[].protected` | Item em `rules/startup-exclusions.json`. Aparece na tela 5 bloqueado. É o que impede desativar o agente de backup ou o driver do leitor fiscal |
| `tool.runtime` | `dotnet` ou `powershell`. Auditoria de relatório contestado: qual implementação produziu este número |

---

## Campos que existem só para o inventário do relatório

Não alimentam regra. Sobrevivem porque a seção "inventário detalhado" do relatório individual e o XLSX do consolidador os consomem, e porque são o que o cliente confere para acreditar no resto.

`machine.manufacturer` · `machine.model` · `machine.productSerial` · `machine.uuid` (chave de deduplicação no consolidador) · `cpu.name` · `memory.modules[]` · `storage.physicalDisks[]` · `storage.volumes[]` · `devices.videoControllers[]` · `os.displayVersion` · `os.installDate` · `network.adapters[]` · `software.programs[]` · `accounts.localAccounts[]`

**`devices.problemDeviceCount`** é o único aqui com candidatura a regra: dispositivo com erro no Gerenciador de Dispositivos é achado legítimo, mas ainda não tem `clientText` aprovado pelo comercial. Registrado como HW-006 com `enabled: false` — o mecanismo previsto pelo doc 03 §1.3.

---

## O que foi deliberadamente deixado de fora

| Não coletado | Motivo |
|---|---|
| Temperatura de CPU | `MSAcpi_ThermalZoneTemperature` raramente é implementada corretamente em hardware de consumo, e quando responde devolve zona ACPI irrelevante. Prometer e entregar número errado é pior que omitir |
| SMART detalhado (horas ligado, setores realocados, desgaste de SSD) | Exigiria embutir `smartctl` — ADR-004 |
| Chave de produto, mesmo parcial | Proibição absoluta do doc funcional §7.1. `SoftwareLicensingProduct.PartialProductKey` **não** é coletado |
| Senhas de Wi-Fi, credenciais de qualquer tipo | Proibição absoluta |
| Nomes de arquivo em Documentos, Desktop, Downloads | Tamanho de pasta somado, sim. Nome de arquivo, nunca |
| Canal Security do Event Log, eventos de logon | Fora do escopo de privacidade |
| Histórico, favoritos, cookies de navegador | Proibição absoluta. Do navegador coletamos apenas nome e versão do programa instalado, da chave `Uninstall` |
