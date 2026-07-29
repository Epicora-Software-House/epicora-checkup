# Epicora Checkup — Especificação Funcional

**Versão do documento:** 1.0
**Data:** 29/07/2026
**Público-alvo:** direção, comercial, equipe técnica de campo, equipe de desenvolvimento
**Documento irmão:** `02-especificacao-tecnica.md`, `03-matriz-riscos-otimizacoes.md`

---

## 1. O que é

O **Epicora Checkup** é um executável portátil para Windows que, rodando por alguns minutos em uma estação de trabalho, produz três coisas:

1. Um **inventário completo** de hardware, sistema, software, rede e configuração de segurança daquela máquina.
2. Uma **lista de riscos e pontos de atenção** derivada desse inventário, em linguagem de cliente, com severidade e ação recomendada.
3. Opcionalmente, e somente com autorização explícita item por item, um **conjunto de otimizações seguras** aplicadas na máquina, com medição de antes e depois.

Não é um agente, não fica residente, não instala nada, não abre porta de rede e não roda em segundo plano. É uma ferramenta de campo, de uso pontual, operada por um técnico presencialmente ou por acesso remoto.

### Nomenclatura

O termo correto é **coletor portátil de inventário e diagnóstico**. Não é microsserviço (isso é arquitetura de backend distribuído) e não é um serviço do Windows.

- Nome comercial proposto: **Epicora Checkup**
- Binário: `EpicoraCheckup.exe`
- Repositório: `epicora-checkup`

---

## 2. Para que serve, comercialmente

O Checkup é o instrumento de coleta do **diagnóstico de infraestrutura** já estruturado como produto de entrada da vertical de TI. Ele resolve o gargalo do diagnóstico: sem ferramenta, levantar 50 máquinas é dias de trabalho manual e o resultado é inconsistente entre técnicos.

Ele sustenta quatro movimentos comerciais:

| Movimento | Como o Checkup sustenta |
|---|---|
| Vender o diagnóstico avulso | Entregável profissional por máquina + relatório executivo do parque |
| Vender o recorrente (MSP) | Evidência quantificada do risco atual: "18 das 50 máquinas não migram para Win11" |
| Vender projeto de hardware | Slots de RAM livres, tipo de disco, veredito manter/upgrade/substituir por máquina |
| Provar competência na primeira visita | Otimização resolve a lentidão percebida no mesmo dia |

### A tensão que precisa ser gerenciada

Se a otimização gratuita resolve a lentidão, o cliente pode concluir que o problema acabou. O contorno é editorial, não técnico: **o relatório precisa separar explicitamente sintoma de causa.** A limpeza trata sintoma. O relatório declara o que ela *não* resolveu — disco HDD, RAM insuficiente, Windows fora de suporte, ausência de backup, usuário como administrador local.

Não tenho como afirmar qual efeito pesa mais no mercado de Chapecó. É teste de campo, e vale acompanhar a taxa de conversão dos primeiros dez diagnósticos.

---

## 3. Atores

| Ator | Papel |
|---|---|
| **Técnico Epicora** | Único operador da ferramenta. Baixa, executa, preenche dados manuais, decide quais otimizações autorizar, leva os arquivos de saída. |
| **Responsável pela máquina** | Usuário do dia a dia. Precisa consentir com ações destrutivas (Lixeira, cache de navegador). Não opera a ferramenta. |
| **Responsável de TI do cliente** | Assina o termo de diagnóstico antes de qualquer execução. Pode acompanhar. |
| **Analista Epicora** | Consolida os JSONs no escritório e produz o relatório executivo. |

O Checkup **não** é ferramenta de autoatendimento. O cliente não baixa e não roda sozinho.

---

## 4. Distribuição e execução

Decisão: **sem pendrive.** Distribuição via GitHub Releases, download na hora, na máquina do cliente.

### Fluxo do técnico

1. Abre o navegador na máquina do cliente e acessa a URL curta de download.
2. Baixa `EpicoraCheckup.exe` (arquivo único, sem instalador, sem dependências).
3. Executa como Administrador.
4. A ferramenta verifica a própria versão contra o release mais recente e avisa se estiver desatualizada.
5. Roda. Ao final, os arquivos de saída ficam em uma pasta local; o técnico copia para o Drive da Epicora.

### Princípios da distribuição

- **Sempre a versão mais recente.** O técnico nunca carrega uma cópia antiga.
- **Zero instalação.** Nada é escrito em `Program Files`, nada em serviços, nada no registro de inicialização.
- **Executável único.** Se a ferramenta precisar de um binário auxiliar (por exemplo `smartctl`), ele vai embutido como recurso e é extraído em pasta temporária, não distribuído solto.
- **Funciona sem internet depois de baixado.** A verificação de versão é opcional e falha silenciosamente se não houver rede.

O tratamento de repositório público vs. privado, SmartScreen, antivírus e assinatura de código está no documento técnico, seção 8. **Isso é o maior risco operacional do projeto** e precisa de decisão da direção antes da Fase 3.

---

## 5. Fluxo de telas

Sete telas, lineares, com voltar habilitado até a tela 5.

### Tela 1 — Identificação
Campos: técnico responsável, empresa cliente, unidade/filial, número do diagnóstico. Persistidos localmente para não redigitar em cada máquina da mesma visita.

Exibe também um aviso permanente e não dispensável:

> Esta ferramenta lê apenas metadados de hardware, software e configuração. Não acessa conteúdo de arquivos, e-mails, mensagens ou histórico de navegação.

Botão único: **Iniciar coleta**.

### Tela 2 — Coleta em andamento
Lista de etapas com estado visível em tempo real. Cada etapa mostra um de cinco estados:

- **Pendente** — aguardando
- **Executando** — em curso, com indicador de atividade
- **Concluído** — sucesso, com resumo de uma linha ("14 programas na inicialização")
- **Ignorado** — não aplicável (ex.: bateria em desktop) ou sem privilégio
- **Falhou** — com o motivo em uma linha

Etapas previstas: Identificação da máquina · Processador e memória · Armazenamento e saúde de disco · Placa de vídeo e dispositivos · Sistema operacional e licenciamento · Compatibilidade com Windows 11 · Segurança e criptografia · Antivírus · Atualizações do Windows · Software instalado · Programas de inicialização · Rede · Contas e privilégios · Bateria · Eventos críticos

Regra dura: **nenhuma etapa que falhe interrompe a coleta.** A ferramenta sempre chega ao fim e sempre produz relatório, mesmo parcial. Toda etapa não concluída aparece no relatório como *"não foi possível verificar"* — nunca como resultado negativo.

Ao final, mostra tempo total decorrido. Meta: menos de 90 segundos em máquina típica.

### Tela 3 — Riscos e pontos de atenção
A tela mais importante da ferramenta.

- **Score da máquina** (0–100) e semáforo: Verde / Amarelo / Vermelho
- **Veredito**: Manter · Fazer upgrade · Substituir
- **Lista de achados** agrupada por severidade (Crítico, Alto, Médio, Baixo, Informativo), cada um com texto de cliente e ação recomendada
- **Bloco separado**: "Não foi possível verificar", listando o que ficou em aberto e por quê

O técnico pode marcar achados como **falso positivo** com justificativa. Isso vai para o JSON e alimenta a melhoria das regras.

### Tela 4 — Dados manuais
Campos preenchidos pelo técnico, obrigatórios os três primeiros:

- Nome/identificação da máquina no padrão do cliente
- Responsável / usuário principal
- Setor e localização física
- Etiqueta de patrimônio
- Situação física observada (limpeza interna, ruído, teclado, tela, cabos)
- Observações livres do técnico

Estes campos são o que amarra o inventário à realidade da empresa. Sem eles o relatório é uma lista de números sem dono.

### Tela 5 — Otimização (opcional)
Aparece somente se houver otimizações aplicáveis. Pode ser inteiramente pulada.

Formato: lista de ações, cada uma **desmarcada por padrão**, com:

- Nome da ação em linguagem clara
- Ganho estimado (espaço, itens, quando mensurável)
- Marcação **IRREVERSÍVEL** quando for o caso
- Marcação **REQUER CONSENTIMENTO DO USUÁRIO** quando for o caso

Proibido: botão "otimizar tudo", "selecionar todos" ou qualquer marcação em lote. O técnico marca item por item. Isso é decisão de projeto, não preferência de interface.

Rodapé permanente:

> A responsabilidade pelo backup dos dados desta máquina é do cliente. Confirme com o responsável antes de prosseguir.

Botões: **Pular otimização** · **Aplicar itens selecionados**.

### Tela 6 — Resultado da otimização
Só aparece se a tela 5 foi executada. Mostra, por ação: sucesso / falha / parcial, e o ganho real medido.

Resumo: espaço liberado, itens de inicialização desativados, ponto de restauração criado (sim/não, com identificador).

### Tela 7 — Salvar e encerrar
Mostra o caminho dos arquivos gerados, com botão para abrir a pasta. Nada é enviado automaticamente para nenhum servidor.

---

## 6. Princípios de projeto

Estes seis princípios são vinculantes. Qualquer decisão de implementação que os contrarie deve ser escalada, não resolvida no código.

**1. Somente leitura por padrão.**
O diagnóstico nunca altera a máquina. Escrita só ocorre na tela 5, com autorização explícita.

**2. Medir antes de agir.**
O estado inicial é capturado e gravado *antes* de qualquer otimização. Sem isso, a limpeza destrói a própria evidência que justifica a proposta comercial.

**3. "Não sei" é um resultado válido e obrigatório.**
Todo campo e toda regra têm três estados: conforme, não conforme, **indeterminado**. Nunca inferir "não conforme" a partir de falha de coleta. Uma regra com falso positivo destrói credibilidade — se o relatório diz "sem antivírus" e o cliente tem um EDR que a ferramenta não detectou, a reunião está perdida.

**4. Reversibilidade.**
Ponto de restauração antes de qualquer alteração de configuração. Todo valor original registrado no log antes de ser modificado.

**5. Rastreabilidade total.**
Cada ação executada vai para o JSON com timestamp, resultado e usuário que autorizou.

**6. Linguagem de cliente.**
Todo texto exibido e todo texto de relatório é escrito para quem vai assinar a proposta, não para quem escreve o código. Termos técnicos, quando necessários, vêm com explicação de uma linha.

---

## 7. O que a ferramenta NÃO deve fazer

### 7.1 Privacidade e escopo — proibições absolutas

Nunca, em nenhuma versão:

- Ler conteúdo de arquivos do usuário. Metadados de disco e pastas, sim. Conteúdo, nunca.
- Ler e-mails, mensagens, histórico ou favoritos de navegador, cookies ou senhas salvas.
- Capturar tela, teclado, áudio, vídeo ou webcam.
- Enumerar arquivos pessoais por nome em `Documentos`, `Desktop`, `Downloads`. Somamos tamanho de pasta quando necessário; não listamos nomes.
- Coletar credenciais de qualquer tipo, incluindo chaves de produto de terceiros e senhas de Wi-Fi.
- Enviar qualquer dado para qualquer servidor sem ação explícita do técnico. A ferramenta não faz telemetria.
- Abrir porta de escuta, criar serviço, criar tarefa agendada, criar item de inicialização ou qualquer forma de persistência.
- Instalar agente de acesso remoto.

Estas proibições devem constar por escrito no termo de diagnóstico assinado antes de qualquer execução, alinhado com LGPD. **Redação jurídica é responsabilidade de quem cuida do jurídico da Epicora**, não da equipe de desenvolvimento.

### 7.2 Otimização — lista negra

As ações abaixo são proibidas, **mesmo que qualquer guia de otimização da internet as recomende**:

| Ação proibida | Motivo |
|---|---|
| Desativar Windows Update | Cria buraco de segurança e transfere a responsabilidade para a Epicora |
| Desativar Windows Defender | Se é o único antivírus, deixa a máquina exposta |
| Desativar Windows Search | Quebra a busca de e-mail no Outlook. Gera chamado no dia seguinte |
| Desativar SysMain em máquina com HDD | Nesse caso o SysMain ajuda; desativar piora |
| Desfragmentar SSD | Desgasta o disco sem ganho |
| Desativar Restauração do Sistema | Remove justamente a rede de segurança da própria ferramenta |
| "Tweaks" de registro para performance | Ganho não comprovado, risco real |
| Mexer em serviços em bloco | Impossível prever dependências |
| Desativar UAC | Redução direta de postura de segurança |
| Limpar `%SystemRoot%\Installer` ou WinSxS manualmente | Quebra desinstalação e atualização de pacotes MSI |
| Excluir arquivos de perfil de usuário | Fora de escopo, risco de perda de dados |
| Desinstalar qualquer software | Fora de escopo. A ferramenta reporta, o técnico decide separadamente |
| Alterar configuração de rede, DNS, IP, proxy | Fora de escopo |
| Aplicar política de domínio ou GPO | Fora de escopo total |

### 7.3 Observação de honestidade técnica

A maior parte do ganho percebido de "otimização" vem de **liberar espaço em disco** e **reduzir programas de inicialização**. Desativar serviços do Windows rende quase nada em hardware da última década e concentra praticamente todo o risco.

Não tenho números confiáveis para quantificar isso — recomendo que a equipe meça nas primeiras dez máquinas antes de acreditar em qualquer estimativa, minha ou de qualquer guia. Mas a assimetria entre ganho e risco é clara o bastante para justificar cortar a parte de serviços.

### 7.4 A armadilha mais provável: itens de inicialização

Cliente de backup, agente de VPN, agente de EDR, ERP com componente residente, driver de leitor fiscal — todos parecem "programa desnecessário na inicialização" para quem olha rápido.

Regras obrigatórias:
- Lista de exclusão por nome de processo e fabricante, versionada no repositório
- Nunca desativar item de fabricante desconhecido sem o técnico confirmar
- Valor original sempre registrado antes de desativar
- Nunca desativar mais de um item sem marcação individual

---

## 8. Saídas

Todas gravadas em `.\EpicoraCheckup\<CLIENTE>\`, ao lado do executável.

| Arquivo | Formato | Finalidade |
|---|---|---|
| `HOSTNAME_SERIAL_AAAAMMDD.json` | JSON | Dado bruto. Fonte única de verdade. Insumo do consolidador. |
| `HOSTNAME_SERIAL_AAAAMMDD.html` | HTML autocontido | Relatório individual legível, entregável por máquina |
| `HOSTNAME_SERIAL_AAAAMMDD.log` | Texto | Log de execução e de todas as ações aplicadas |

### Consolidador (ferramenta separada)

Roda no notebook do analista, não na máquina do cliente. Lê todos os JSONs de uma pasta e produz:

- Relatório executivo do parque
- CSV/XLSX de inventário
- Distribuição de riscos por severidade
- Lista de máquinas que não migram para Windows 11
- Priorização de investimento

O PDF na identidade da Epicora sai da skill de documento já existente, a partir do Markdown gerado pelo consolidador.

---

## 9. Critérios de aceite

A Fase 2 só é considerada pronta quando:

1. O executável roda em Windows 10 22H2 e Windows 11 sem instalar nada.
2. Coleta completa em menos de 90 segundos em máquina típica.
3. A UI nunca congela durante a coleta.
4. Toda falha de etapa individual é isolada; a ferramenta sempre gera relatório.
5. Executado **sem** privilégio de administrador, a ferramenta roda, marca as etapas privilegiadas como "Ignorado — sem privilégio" e ainda gera relatório útil.
6. Nenhuma escrita fora da pasta de saída e da pasta temporária, verificado com monitor de sistema de arquivos.
7. Testado em, no mínimo: um desktop antigo com HDD, um notebook com SSD, uma máquina em domínio, uma máquina com EDR de terceiro, uma máquina sem TPM.
8. Zero falso positivo nas dez primeiras máquinas reais, ou regra corrigida antes de qualquer uso comercial.

---

## 10. Fora de escopo desta versão

Registrado para não haver dúvida: coleta remota sem presença do técnico · agente residente · monitoramento contínuo · portal web · banco de dados central · integração com sistema de tickets · suporte a Linux ou macOS · coleta de servidores · aplicação de correções ou atualizações · inventário de licenças com validação junto ao fabricante.

Vários destes são caminhos naturais de evolução. Nenhum entra agora.

---

## 11. Fases

| Fase | Entrega | Depende de |
|---|---|---|
| 0 | Modelo de dados fechado: cada campo mapeado à decisão comercial que sustenta | — |
| 1 | Protótipo em PowerShell, testado em 5–10 máquinas reais | Fase 0 |
| 2 | Executável C#/WinForms: telas 1–4 e 7, JSON e HTML | Fase 1 |
| 3 | Distribuição via GitHub Releases, verificação de versão, assinatura de código | Fase 2 + decisão da direção sobre repositório e certificado |
| 4 | Consolidador e template de relatório de marca | Fase 2 |
| 5 | **Otimização** (telas 5 e 6) | Fase 4 concluída e validada |

**Por que a otimização é a última fase.** Enquanto a ferramenta é somente-leitura, o pior caso é um relatório errado. Quando ela escreve, o pior caso é a máquina do cliente parar de funcionar na frente do técnico. As duas coisas não podem ser validadas na mesma etapa.

A Fase 5 exige, antes de qualquer cliente: validação em máquina virtual descartável, depois em todas as máquinas internas da Epicora, e cláusula de autorização de alteração de configuração no termo de diagnóstico.
