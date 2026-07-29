# Epicora Checkup — Matriz de Riscos e Catálogo de Otimizações

**Versão do documento:** 1.0
**Data:** 29/07/2026
**Público-alvo:** equipe de desenvolvimento, equipe técnica de campo, comercial
**Documento irmão:** `01-especificacao-funcional.md`, `02-especificacao-tecnica.md`

---

## Por que este documento é o mais importante dos três

O inventário é matéria-prima. **A matriz é o que transforma inventário em proposta comercial.**

Cada regra aqui alimenta quatro coisas de uma vez: a tela de riscos, o relatório individual, o score da máquina e o relatório executivo do parque. E o campo `clientText` de cada regra vai, com pouca ou nenhuma edição, direto para dentro da proposta.

Por isso os textos são escritos em linguagem de cliente, não de técnico. Quem revisa este documento não é só quem programa — é quem vende.

---

## 1. Regras de operação do motor

**1.1 Três estados, sempre.** Toda regra resolve para `Compliant`, `NonCompliant` ou `Indeterminate`. Se o dado necessário não foi coletado, o resultado é `Indeterminate` — nunca `NonCompliant`.

**1.2 `Indeterminate` não pontua no score.** Aparece no bloco "não foi possível verificar" do relatório, com o motivo.

**1.3 Regra sem texto de cliente aprovado não entra em release.** O campo `clientText` é revisado pelo comercial, não só pelo dev.

**1.4 Falso positivo é bug de severidade máxima.** Se o relatório diz "sem antivírus" e o cliente tem um EDR que a ferramenta não detectou, a reunião está perdida. Regra que gerou falso positivo em campo é desativada no mesmo dia e corrigida antes de voltar.

**1.5 Regras são declarativas e versionadas.** Vivem em `rules/*.json`, não em `if` no código. Mudança de regra não deve exigir recompilar.

**1.6 Nenhuma regra dispara otimização automaticamente.** O vínculo `linkedOptimizations` apenas *sugere* na tela 5, sempre desmarcado.

---

## 2. Formato de regra

```jsonc
{
  "id": "STO-001",
  "version": 1,
  "enabled": true,
  "category": "Armazenamento",
  "severity": "Critical",              // Critical | High | Medium | Low | Info
  "weight": 25,                         // pontos subtraídos do score
  "requires": [                         // se algum caminho for null → Indeterminate
    "collectors.storage.data.systemDisk.mediaType"
  ],
  "condition": {
    "path": "collectors.storage.data.systemDisk.mediaType",
    "operator": "equals",
    "value": "HDD"
  },
  "title": "Disco de sistema é HDD",
  "clientText": "...",
  "recommendedAction": "...",
  "evidenceFields": ["collectors.storage.data.systemDisk.model"],
  "linkedOptimizations": [],
  "verdictInfluence": "Replace"         // null | Upgrade | Replace
}
```

Operadores mínimos suportados: `equals`, `notEquals`, `lessThan`, `greaterThan`, `contains`, `notContains`, `isTrue`, `isFalse`, `isNull`, `inList`, `notInList`. Composição por `allOf` / `anyOf` / `not`.

**Recomendação de projeto:** manter o conjunto de operadores pequeno e recusar a tentação de embutir uma linguagem de expressão. Se uma regra não cabe em operadores simples, ela provavelmente precisa de um campo derivado calculado no coletor — que é mais testável.

---

## 3. Modelo de score

Score inicial: **100**. Cada regra `NonCompliant` subtrai seu `weight`. Piso em 0.

| Faixa | Score | Significado |
|---|---|---|
| **Verde** | 80–100 | Máquina saudável. Manutenção de rotina. |
| **Amarelo** | 50–79 | Pontos de atenção. Ação recomendada no curto prazo. |
| **Vermelho** | 0–49 | Risco relevante. Ação necessária. |

### Veredito

Calculado por precedência, não pelo score:

1. Se qualquer regra com `verdictInfluence = "Replace"` estiver `NonCompliant` → **Substituir**
2. Senão, se qualquer regra com `verdictInfluence = "Upgrade"` estiver `NonCompliant` → **Fazer upgrade**
3. Senão → **Manter**

Motivo de separar veredito de score: uma máquina pode ter score razoável e ainda assim ser incapaz de rodar Windows 11. O score mede saúde geral; o veredito responde à pergunta que o cliente faz.

### Calibração

Os pesos abaixo são **ponto de partida, não verdade.** Precisam ser recalibrados depois das dez primeiras máquinas reais. Sinal de que estão errados: se todas as máquinas de um cliente saem Vermelho, o relatório perde poder de discriminação e o cliente para de acreditar.

---

## 4. Matriz de regras

Legenda de coluna **Verd.**: influência no veredito. `S` = Substituir, `U` = Upgrade, `—` = nenhuma.

### 4.1 Armazenamento

| ID | Condição | Sev. | Peso | Verd. | Otim. |
|---|---|---|---|---|---|
| STO-001 | Disco de sistema é HDD | Crítico | 25 | S | — |
| STO-002 | Espaço livre em C: abaixo de 10% | Crítico | 20 | — | OPT-TEMP, OPT-WU, OPT-OLD, OPT-BIN |
| STO-003 | Espaço livre em C: entre 10% e 20% | Alto | 10 | — | OPT-TEMP, OPT-WU |
| STO-004 | SMART indica falha prevista | Crítico | 30 | S | — |
| STO-005 | `HealthStatus` do disco diferente de saudável | Alto | 15 | — | — |
| STO-006 | SSD sem TRIM habilitado | Baixo | 3 | — | OPT-TRIM |
| STO-007 | HDD com fragmentação relevante | Baixo | 3 | — | OPT-DEFRAG |
| STO-008 | Capacidade total do disco de sistema abaixo de 240 GB | Médio | 6 | U | — |

**STO-001, clientText:**
> Esta máquina utiliza um disco rígido mecânico (HDD) como disco principal. Essa é, na prática, a maior causa de lentidão em computadores que ainda têm processador e memória adequados. A substituição por um disco SSD costuma reduzir de forma expressiva o tempo de inicialização e de abertura de programas, com custo baixo em relação ao ganho.
>
> **Ação recomendada:** substituir o disco de sistema por SSD e migrar o sistema operacional.

**STO-004, clientText:**
> O próprio disco desta máquina está reportando previsão de falha. Isso indica risco concreto e imediato de perda de dados. O disco deve ser substituído e os dados copiados antes disso, com prioridade sobre qualquer outra ação nesta máquina.
>
> **Ação recomendada:** backup imediato dos dados e substituição do disco.

**STO-002, clientText:**
> O disco desta máquina está com menos de 10% de espaço livre. Além de causar lentidão, essa condição pode impedir a instalação de atualizações de segurança do Windows e, em casos extremos, travar a inicialização do sistema.
>
> **Ação recomendada:** liberar espaço agora e avaliar aumento de capacidade ou centralização de arquivos em servidor.

STO-006 depende de detectar o estado do TRIM. **Confiança baixa** sobre o caminho exato; se não for confiável, deixar a regra desabilitada na v1 em vez de arriscar falso positivo por um achado de peso 3.

### 4.2 Memória

| ID | Condição | Sev. | Peso | Verd. | Otim. |
|---|---|---|---|---|---|
| MEM-001 | RAM total igual ou inferior a 4 GB | Crítico | 20 | S | — |
| MEM-002 | RAM total igual ou inferior a 8 GB | Alto | 12 | U | — |
| MEM-003 | RAM ≤ 8 GB **e** existe slot livre | Alto | 12 | U | — |
| MEM-004 | RAM ≤ 8 GB **e** nenhum slot livre | Alto | 14 | S | — |
| MEM-005 | Pentes com velocidades diferentes | Baixo | 2 | — | — |

MEM-003 e MEM-004 são a mesma condição de RAM com desfechos comerciais opostos, e é exatamente por isso que a contagem de slots livres importa tanto. Implementar como mutuamente exclusivas e não somar peso com MEM-002.

**MEM-003, clientText:**
> Esta máquina possui 8 GB de memória, quantidade que hoje limita o uso simultâneo de navegador, sistema de gestão e planilhas. Verificamos que ela tem slot de memória livre, o que permite ampliar a capacidade sem substituir o computador — é o upgrade com melhor relação custo-benefício disponível nesta máquina.
>
> **Ação recomendada:** instalar módulo adicional de memória compatível.

**MEM-004, clientText:**
> Esta máquina possui 8 GB de memória e todos os slots já estão ocupados. Ampliar a memória exigiria substituir os módulos existentes, o que reduz muito o benefício do investimento. Considerando o conjunto, a substituição da máquina tende a ser mais vantajosa.
>
> **Ação recomendada:** avaliar substituição da máquina.

### 4.3 Processador

| ID | Condição | Sev. | Peso | Verd. | Otim. |
|---|---|---|---|---|---|
| CPU-001 | 2 núcleos físicos ou menos | Alto | 15 | S | — |
| CPU-002 | CPU fora da lista de suportados para Windows 11 | Crítico | 20 | S | — |
| CPU-003 | Virtualização desabilitada no firmware | Info | 0 | — | — |

CPU-002 depende de embutir a lista oficial de CPUs suportadas (ver ponto aberto 6 do documento técnico). **Se a lista não for embutida, esta regra deve nascer desabilitada** e o relatório precisa declarar que a compatibilidade de processador não foi avaliada. Afirmar incompatibilidade sem base é o tipo de erro que custa um cliente.

CPU-003 é informativo, peso zero. Só entra no relatório se houver contexto de virtualização.

### 4.4 Sistema operacional

| ID | Condição | Sev. | Peso | Verd. | Otim. |
|---|---|---|---|---|---|
| OS-001 | Windows 10 ou anterior | Crítico | 25 | — | — |
| OS-002 | Windows fora de suporte (versão anterior a Win10) | Crítico | 30 | S | — |
| OS-003 | Windows não ativado | Alto | 15 | — | — |
| OS-004 | Edição Home em ambiente corporativo | Médio | 8 | — | — |
| OS-005 | Build desatualizada em relação à mais recente conhecida | Alto | 10 | — | — |
| OS-006 | Uptime acima de 30 dias | Baixo | 2 | — | — |
| OS-007 | Sistema instalado há mais de 5 anos sem reinstalação | Baixo | 3 | — | — |

**OS-001, clientText:**
> Esta máquina roda Windows 10, cujo suporte gratuito da Microsoft foi encerrado em outubro de 2025. Isso significa que ela deixou de receber correções de segurança pelo canal padrão. Máquinas nessa condição são alvo preferencial de ataques, porque as falhas descobertas depois do fim do suporte permanecem abertas.
>
> **Ação recomendada:** migrar para Windows 11, quando o hardware permitir, ou substituir a máquina.

**Nota de verificação:** o encerramento do suporte ao Windows 10 em 14 de outubro de 2025 está correto. Já o **programa de Extended Security Updates (ESU) para consumidores e empresas pode ter mudado de condições, prazos ou preço desde então.** Antes de usar este texto comercialmente, confirmar o estado atual do ESU na página oficial da Microsoft. Não incluir promessa ou número sobre ESU no relatório sem essa verificação.

**OS-004, clientText:**
> Esta máquina utiliza edição Home do Windows, que não permite ingresso em domínio, criptografia BitLocker completa nem aplicação de políticas centralizadas de segurança. Em ambiente corporativo, isso limita a padronização e o controle do parque.
>
> **Ação recomendada:** avaliar upgrade para edição Pro.

OS-004 só deve disparar se houver indício de ambiente corporativo (máquina em domínio no parque, ou marcação manual). Sem esse contexto, gera ruído.

OS-005 depende da tabela de builds (ponto aberto 5 do documento técnico). Sem tabela mantida, a regra dispara falso positivo. **Se a tabela não for mantida, desabilitar.**

### 4.5 Compatibilidade com Windows 11

| ID | Condição | Sev. | Peso | Verd. | Otim. |
|---|---|---|---|---|---|
| W11-001 | TPM ausente | Crítico | 20 | S | — |
| W11-002 | TPM presente mas versão inferior a 2.0 | Crítico | 20 | S | — |
| W11-003 | TPM 2.0 presente mas desativado no firmware | Alto | 5 | — | — |
| W11-004 | Secure Boot desabilitado, com firmware UEFI | Médio | 5 | — | — |
| W11-005 | Firmware em modo BIOS legado | Alto | 10 | — | — |
| W11-006 | Máquina reprovada em qualquer requisito de Win11 | Crítico | 0 | S | — |

W11-006 é uma **regra agregadora**: peso zero para não somar duas vezes, mas carrega o veredito de substituição e produz a linha que vai para o relatório executivo — "18 das 50 máquinas não migram".

W11-003 tem peso baixo de propósito: TPM desativado no firmware é resolvível com uma visita e alguns minutos na BIOS. É achado de serviço, não de hardware. Distinguir W11-001 de W11-003 é a diferença entre "trocar a máquina" e "habilitar uma opção" — errar isso custa caro nos dois sentidos.

W11-005: BIOS legado pode ser convertido para UEFI sem reinstalar, mas é procedimento com risco e nem todo hardware suporta. Peso médio, veredito neutro, ação recomendada é "avaliar conversão".

### 4.6 Segurança

| ID | Condição | Sev. | Peso | Verd. | Otim. |
|---|---|---|---|---|---|
| SEC-001 | Nenhum antivírus detectado | Crítico | 25 | — | — |
| SEC-002 | Antivírus presente mas com definições desatualizadas | Crítico | 20 | — | — |
| SEC-003 | Proteção em tempo real desabilitada | Crítico | 25 | — | — |
| SEC-004 | Notebook sem BitLocker no disco de sistema | Alto | 15 | — | — |
| SEC-005 | Desktop sem BitLocker no disco de sistema | Médio | 6 | — | — |
| SEC-006 | Firewall desabilitado em qualquer perfil | Alto | 12 | — | — |
| SEC-007 | Usuário do dia a dia é administrador local | Alto | 12 | — | — |
| SEC-008 | SMBv1 habilitado | Alto | 15 | — | — |
| SEC-009 | RDP habilitado sem contexto justificado | Médio | 8 | — | — |
| SEC-010 | Sem atualização de segurança nos últimos 90 dias | Alto | 12 | — | — |
| SEC-011 | Ferramenta de acesso remoto instalada | Info | 0 | — | — |
| SEC-012 | Nenhum agente de backup identificado | Alto | 15 | — | — |

**Atenção máxima em SEC-001, SEC-002 e SEC-003.** Conforme o documento técnico, seção 4.6, a interpretação do estado do antivírus depende de uma máscara de bits não documentada oficialmente pela Microsoft. Requisito vinculante:

> Se a interpretação do estado do antivírus não for inequívoca, o resultado é `Indeterminate`. Jamais `NonCompliant`.

Além disso, SEC-001 deve cruzar com a lista de software instalado: se `root\SecurityCenter2` não retornar nada mas a lista de programas contiver um EDR corporativo conhecido, o resultado é `Indeterminate` com a observação "solução de segurança detectada na lista de programas, mas não registrada no Windows Security Center — verificar manualmente". Este cruzamento é obrigatório, não opcional.

**SEC-007, clientText:**
> O usuário que utiliza esta máquina no dia a dia possui privilégio de administrador. Isso significa que qualquer programa executado por ele — inclusive um anexo malicioso aberto por engano — pode alterar o sistema, desativar a proteção e se instalar de forma permanente. Separar a conta de uso diário da conta administrativa é uma das medidas de maior efeito e menor custo em segurança de estações de trabalho.
>
> **Ação recomendada:** criar conta administrativa separada e reduzir o usuário do dia a dia a usuário padrão.

**SEC-008, clientText:**
> Esta máquina tem o protocolo SMBv1 habilitado. Trata-se de um protocolo de compartilhamento de arquivos antigo, com falhas conhecidas e exploradas por famílias de ransomware. A própria Microsoft recomenda desabilitá-lo.
>
> **Ação recomendada:** desabilitar SMBv1 após confirmar que nenhum equipamento antigo da rede depende dele.

A ressalva final de SEC-008 é essencial: impressoras e equipamentos industriais antigos às vezes só falam SMBv1. Por isso **não existe otimização vinculada** — desabilitar SMBv1 é projeto com validação, não clique na tela 5.

**SEC-012, clientText:**
> Não identificamos nenhuma solução de backup ativa nesta máquina. Se o disco falhar, se houver furto ou se um ransomware criptografar os arquivos, os dados armazenados localmente não têm rota de recuperação.
>
> **Ação recomendada:** definir política de backup — centralização em servidor, backup em nuvem, ou ambos.

SEC-012 tem alta taxa de falso negativo: backup pode existir por mecanismo que a ferramenta não detecta. Redigir sempre como "não identificamos", nunca como "não existe".

### 4.7 Software e inicialização

| ID | Condição | Sev. | Peso | Verd. | Otim. |
|---|---|---|---|---|---|
| SW-001 | Mais de 15 itens de inicialização | Baixo | 5 | — | OPT-STARTUP |
| SW-002 | Mais de 25 itens de inicialização | Médio | 8 | — | OPT-STARTUP |
| SW-003 | Navegador em versão desatualizada | Médio | 6 | — | — |
| SW-004 | Software com indício de falta de licença | Alto | 10 | — | — |
| SW-005 | Mais de uma solução de antivírus instalada | Alto | 12 | — | — |
| SW-006 | Runtime obsoleto instalado (Java antigo, Flash, .NET fora de suporte) | Médio | 6 | — | — |
| SW-007 | Software potencialmente indesejado (toolbar, "otimizador") | Médio | 8 | — | — |

SW-004 exige extremo cuidado. Nunca afirmar irregularidade. Texto obrigatório no formato:
> Identificamos instalações que podem exigir revisão de licenciamento. Recomendamos conferência interna dos comprovantes.

Afirmar pirataria com base em heurística de lista de programas é exposição jurídica desnecessária para a Epicora.

SW-005 é achado relevante e frequente: dois antivíruses ativos degradam desempenho e podem se neutralizar. Exige distinguir antivírus ativo de resíduo de desinstalação incompleta.

SW-007 nunca deve gerar desinstalação automática. A ferramenta reporta; o técnico decide fora dela.

### 4.8 Rede

| ID | Condição | Sev. | Peso | Verd. | Otim. |
|---|---|---|---|---|---|
| NET-001 | Adaptador gigabit negociando 100 Mbps ou menos | Médio | 6 | — | — |
| NET-002 | Máquina fixa conectada por Wi-Fi | Baixo | 3 | — | — |
| NET-003 | IP configurado manualmente fora de padrão | Baixo | 2 | — | — |
| NET-004 | DNS configurado apontando para servidor público em rede com domínio | Médio | 6 | — | — |

**NET-001, clientText:**
> A placa de rede desta máquina suporta 1 Gbps, mas está negociando velocidade inferior. Isso normalmente indica cabo danificado, conector mal crimpado ou porta de switch limitada — e resulta em lentidão perceptível no acesso a arquivos em rede, mesmo com internet rápida.
>
> **Ação recomendada:** verificar cabeamento e porta do switch.

NET-001 é gancho direto para a vertical de infraestrutura de rede: é um achado por máquina que, repetido no parque, vira diagnóstico de cabeamento.

NET-004 vale destacar: máquina em domínio com DNS público configurado tipicamente quebra resolução de nomes internos e é causa de problemas intermitentes difíceis de diagnosticar.

### 4.9 Hardware físico e bateria

| ID | Condição | Sev. | Peso | Verd. | Otim. |
|---|---|---|---|---|---|
| HW-001 | Desgaste de bateria acima de 30% | Médio | 8 | — | — |
| HW-002 | Desgaste de bateria acima de 50% | Alto | 12 | — | — |
| HW-003 | BIOS/UEFI com mais de 5 anos sem atualização | Baixo | 3 | — | — |
| HW-004 | Máquina fabricada há mais de 7 anos | Médio | 10 | U | — |
| HW-005 | Máquina fabricada há mais de 10 anos | Alto | 15 | S | — |

HW-004 e HW-005 usam a data do BIOS como aproximação da idade da máquina. **É aproximação, não fato:** BIOS atualizado altera essa data. Sempre redigir como "aproximadamente" e nunca como idade exata. Se a marcação manual do técnico contradisser, a marcação manual prevalece.

### 4.10 Estabilidade

| ID | Condição | Sev. | Peso | Verd. | Otim. |
|---|---|---|---|---|---|
| EST-001 | Desligamentos inesperados nos últimos 30 dias | Alto | 12 | — | — |
| EST-002 | Erros de disco no log de eventos | Crítico | 20 | S | — |
| EST-003 | Erros críticos recorrentes de aplicação | Médio | 6 | — | — |

EST-002 é achado de alto valor: erro de disco no log frequentemente antecede falha física e é evidência mais confiável que o SMART básico. Somar com STO-004 quando ambos disparam — a coincidência dos dois é o sinal mais forte possível de disco morrendo.

Os IDs de evento correspondentes não estão neste documento porque **não vou registrar números que não posso confirmar**. Devem ser levantados na documentação da Microsoft na Fase 1 e versionados em `rules/event-ids.json`.

---

## 5. Catálogo de otimizações (Fase 5)

**Lembrete vinculante:** nenhuma ação aqui é executada sem marcação individual do técnico. Não existe "otimizar tudo". Não existe "selecionar todos".

Legenda: **Irrev.** = irreversível. **Consent.** = exige consentimento do usuário da máquina, não apenas do técnico.

### 5.1 Ações autorizadas

| ID | Ação | Ganho típico | Irrev. | Consent. | Risco |
|---|---|---|---|---|---|
| OPT-TEMP | Limpar `%TEMP%` e `C:\Windows\Temp` | Espaço | Sim | Não | Baixo |
| OPT-WU | Limpar cache do Windows Update (`SoftwareDistribution\Download`) | Espaço | Sim | Não | Baixo |
| OPT-OLD | Remover `Windows.old`, quando existir | Espaço (alto) | Sim | Sim | Médio |
| OPT-BIN | Esvaziar a Lixeira | Espaço | Sim | **Sim** | Médio |
| OPT-THUMB | Limpar cache de miniaturas e ícones | Espaço | Sim | Não | Baixo |
| OPT-DUMP | Remover dumps de erro e logs antigos do Windows | Espaço | Sim | Não | Baixo |
| OPT-BROWSER | Limpar cache de navegadores | Espaço | Sim | **Sim** | Médio |
| OPT-TRIM | Executar TRIM em SSD | Desempenho | Não | Não | Baixo |
| OPT-DEFRAG | Desfragmentar HDD | Desempenho | Não | Não | Baixo |
| OPT-STARTUP | Desativar itens de inicialização selecionados | Desempenho | Não | Não | **Alto** |

### 5.2 Notas por ação

**OPT-OLD — normalmente o maior ganho isolado.** `Windows.old` pode ocupar dezenas de GB. Mas é a pasta que permite reverter uma atualização de versão do Windows. Se a máquina foi atualizada recentemente, remover elimina essa rota de volta. Exigir consentimento e exibir a data da atualização na tela.

**OPT-BIN — consentimento do usuário é obrigatório e não delegável.** A Lixeira frequentemente contém arquivo que o usuário apagou "por enquanto". Autorização do técnico não substitui a do dono dos arquivos.

**OPT-BROWSER — pode encerrar sessões abertas.** O usuário perde login em sistemas web e pode não saber a senha. Avisar explicitamente e exigir consentimento.

**OPT-DEFRAG — verificar tipo de mídia antes.** Desfragmentar SSD é proibido (ver lista negra no documento funcional). Se o tipo de mídia estiver `Indeterminate`, **não executar**. Este é um caso em que "não sei" tem que bloquear a ação, não permitir.

**OPT-STARTUP — a ação de maior risco de todo o catálogo.** Cliente de backup, agente de VPN, agente de EDR, ERP com componente residente, driver de leitor fiscal — todos parecem "programa desnecessário na inicialização".

Controles obrigatórios:
1. Lista de exclusão por nome de processo e por fabricante, versionada em `rules/startup-exclusions.json`. Itens na lista aparecem, mas com marcação de bloqueio e não podem ser selecionados.
2. Item de fabricante desconhecido ou sem assinatura digital exige confirmação adicional em diálogo separado.
3. Valor original gravado no log **antes** da alteração.
4. Marcação individual obrigatória. Sem seleção em lote, em nenhuma circunstância.
5. Ver ponto aberto 7 do documento técnico: preferir mover a entrada para chave de backup própria da Epicora, em vez de escrever no formato binário não documentado de `StartupApproved`.

### 5.3 Protocolo de execução

Ordem fixa, imposta pelo orquestrador:

1. **Medir estado inicial** e gravar no JSON. Sem isso, nenhuma ação executa.
2. **Criar ponto de restauração.** Se falhar, exibir aviso e exigir confirmação explícita para prosseguir sem rede de segurança. Nunca prosseguir silenciosamente.
3. Executar as ações marcadas, em sequência, cada uma isolada em try/catch. Falha de uma não aborta as demais.
4. **Medir estado final** e calcular o ganho real.
5. Gravar tudo: ação, quem autorizou, valores originais, resultado, ganho medido.

**Por que medir antes é obrigatório:** se a limpeza liberar 14 GB antes do relatório ser gerado, o achado STO-002 ("espaço livre abaixo de 10%") desaparece e a Epicora destrói a própria evidência que justifica a proposta de aumento de capacidade. O número medido também é o que vende: "liberamos 14,2 GB nesta máquina; no parque de 50 máquinas isso representa X".

### 5.4 O que o relatório precisa dizer depois da otimização

Bloco obrigatório, com este espírito:

> **O que foi resolvido hoje:** liberamos 14,2 GB de espaço em disco e reduzimos de 22 para 9 os programas que iniciam junto com o Windows. A máquina deve apresentar melhora perceptível na inicialização e na abertura de programas.
>
> **O que estas ações não resolvem:** a lentidão desta máquina tem causa estrutural que a limpeza não altera — o disco principal é mecânico (HDD) e a memória instalada é de 8 GB. Além disso, permanecem em aberto os seguintes pontos de risco: sistema operacional fora do suporte da Microsoft, ausência de rotina de backup identificada, e usuário do dia a dia com privilégio de administrador.

Essa separação entre **sintoma tratado** e **causa remanescente** é o que impede a otimização gratuita de competir com a venda. Sem esse bloco, a ferramenta trabalha contra a Epicora.

---

## 6. Governança da matriz

**Revisão:** a cada dez diagnósticos realizados, ou imediatamente após qualquer falso positivo em campo.

**Registro de falso positivo:** o técnico marca na tela 3, com justificativa. Isso vai para o JSON. O analista consolida os falsos positivos e alimenta a revisão. Este ciclo é o principal mecanismo de melhoria da ferramenta, e depende de o campo de justificativa ser realmente preenchido — vale cobrar isso no procedimento interno.

**Aprovação de texto:** alteração em `clientText` passa pelo comercial antes de entrar em release. O texto é peça de venda, não string de sistema.

**Versionamento:** cada regra tem `version`. Regras nunca são deletadas, apenas marcadas `enabled: false`, para que relatórios antigos permaneçam auditáveis e reprodutíveis.

---

## 7. Incertezas registradas neste documento

- **Todos os pesos e faixas de score são estimativas de calibração inicial.** Não tenho base empírica para eles; foram derivados de julgamento sobre impacto relativo. Recalibrar com dados reais.
- **Os limiares numéricos** (10% de espaço livre, 8 GB de RAM, 15 itens de inicialização, 30% de desgaste de bateria, 7 anos de idade) são convenções razoáveis de mercado, não valores com respaldo em fonte que eu possa citar. Ajustar conforme o parque real dos clientes da Epicora.
- **O fim do suporte ao Windows 10 em 14/10/2025 é fato.** As condições atuais do programa ESU **não são** — verificar antes de qualquer menção comercial.
- **Não incluí IDs de evento do Windows** porque não tenho como confirmá-los. Levantar na doc oficial.
- **Regras que dependem de dados marcados M ou B no documento técnico** (TPM, Secure Boot, antivírus, SMART, TRIM, UEFI, bateria) só devem ser habilitadas depois que a fonte de dados correspondente for validada em campo. Regra habilitada sobre fonte não validada é fábrica de falso positivo.
