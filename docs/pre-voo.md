# Pré-voo — primeira rodada em 10 máquinas

Este é o documento que vai para o técnico. Ele existe porque **nenhuma linha do executável rodou em Windows ainda**: o desenvolvimento acontece em Mac, o CI compila mas não executa contra WMI, e o que está verificado é só o que não depende de máquina — compilação, derivação de campo e conformidade da saída com o schema.

Traduzindo: a ferramenta nunca leu um disco de verdade. **Nada que dependa de coleta é confiável até esta rodada acontecer.**

O pré-voo é o que fecha os critérios de aceite da Fase 2 ([doc 01 §9](01-especificacao-funcional.md)) — que são oito, e nenhum deles pode ser verificado no CI.

> **Não é diagnóstico comercial.** O relatório desta rodada não vai para cliente sem revisão no escritório: a matriz tem **23 das 61 regras habilitadas**, então a lista de riscos sai curta de propósito. Em máquina de cliente, o termo de diagnóstico assinado continua sendo pré-requisito, como em qualquer execução.

---

## O link

**Baixar:**

```
https://github.com/Epicora-Software-House/epicora-checkup/releases/latest/download/EpicoraCheckup.exe
```

Esse endereço não muda: resolve sempre para a versão mais recente publicada. É o único link que o técnico precisa receber.

**Conferir o arquivo** (opcional, e é o que substitui a assinatura digital que a v1 não tem):

```
https://github.com/Epicora-Software-House/epicora-checkup/releases/latest/download/EpicoraCheckup.exe.sha256
```

```powershell
Get-FileHash EpicoraCheckup.exe -Algorithm SHA256
```

O número tem que bater com o do arquivo `.sha256`.

---

## Roteiro na máquina

1. **Baixe o arquivo** pelo link acima, no navegador da própria máquina.

   Se o navegador reclamar que o arquivo "não é baixado com frequência" ou é "incomum", escolha **Manter**. Ele não está assinado, e é isso que o navegador está dizendo — não que tem algo errado com ele.

2. **Deixe o arquivo numa pasta simples**, como `C:\Epicora`. Pode ser a pasta de Downloads também: **os arquivos de saída são criados ao lado do executável**, então convém saber onde ele está.

3. **Clique com o botão direito → Executar como administrador**, se você tiver a senha de administrador da máquina.

   Sem administrador a ferramenta **roda igual**: TPM, BitLocker e a verificação de saúde do disco aparecem como "não foi possível verificar", e o resto é coletado normalmente. O relatório sai parcial e honesto, não sai errado.

4. **O Windows vai avisar que "protegeu seu PC".** É esperado, e não é defeito: o executável não é assinado (decisão registrada, [ADR-003](adr/003-certificado-de-assinatura.md)).

   Clique em **Mais informações** → **Executar assim mesmo**.

5. **A ferramenta pode avisar que está desatualizada**, logo na primeira tela. Se avisar, baixe de novo pelo mesmo link e use a nova — o aviso não bloqueia nada, mas relatório de versão antiga pode estar usando regra já corrigida.

6. **Tela 1 — preencha:** seu nome, a empresa, a unidade e o número do diagnóstico. Marque *"o parque desta empresa é corporativo"* quando houver padronização de TI, mesmo sem domínio.

   Os campos ficam salvos para a próxima máquina da mesma visita.

7. **Iniciar coleta.** Não feche a janela. A meta é menos de 90 segundos; **anote se passar muito disso.**

8. **Passe pelas telas 3 e 4.** Na tela 3, se algum achado estiver errado, marque como **falso positivo** e escreva o motivo — é isso que corrige a regra. Na tela 4, preencha os dados manuais.

9. **Tela 7 — o botão abre a pasta** com os três arquivos gerados (JSON, HTML e log). O caminho é `EpicoraCheckup\<EMPRESA>\`, ao lado do executável.

10. **Compacte a pasta `EpicoraCheckup` inteira e envie** para:

    ```
    <colar aqui o link da pasta no Drive da Epicora>
    ```

    Os três arquivos são necessários — o log é o que permite entender o que aconteceu quando algo dá errado.

---

## Se o antivírus ou o EDR bloquear

Vai acontecer em alguma das dez, e **é informação de pré-voo, não é problema seu**: um executável desconhecido, baixado da internet, lendo hardware e registro é exatamente o perfil que um EDR moderno bloqueia.

O que fazer:

1. **Não desative o antivírus do cliente.** Em nenhuma hipótese, nem "por um minuto".
2. **Anote qual produto bloqueou e a mensagem exata.** Print da tela serve.
3. **Se houver responsável de TI do cliente disponível**, pergunte se ele autoriza uma exclusão temporária para o arquivo. Se negar, encerrou — siga para o caminho alternativo.
4. **Caminho alternativo:** o protótipo PowerShell, que existe exatamente para isso ([ADR-009](adr/009-prototipo-powershell-e-fallback-permanente.md)). Baixe o repositório como ZIP, extraia, e rode:

   ```
   tools\prototype\EpicoraCheckup.bat
   ```

   ```
   https://github.com/Epicora-Software-House/epicora-checkup/archive/refs/heads/main.zip
   ```

   Ele produz o mesmo JSON, sem interface. É PowerShell 5.1, que já vem no Windows — não instala nada.

---

## Os 10 computadores: quais perfis cobrir

Não são dez máquinas quaisquer. O critério de aceite 7 do doc 01 §9 lista cinco perfis, e cada um existe para expor um caminho de código diferente:

| # | Perfil | Por que este |
|---|---|---|
| 1 | **Desktop antigo, com HDD** | O caminho de disco mecânico, fragmentação e SMART — e o cenário que gera o achado comercial mais valioso |
| 2 | **Notebook com SSD** | Bateria, NVMe, e o caso "máquina saudável não gera achado nenhum" |
| 3 | **Máquina em domínio** | Contas, privilégio e a avaliação de edição do Windows por domínio |
| 4 | **Máquina com EDR de terceiro** | O cruzamento antivírus × software. É onde um falso "sem antivírus" perderia uma reunião |
| 5 | **Máquina sem TPM** | Compatibilidade com Windows 11, e o caminho de fonte privilegiada indisponível |
| 6 | **Uma delas rodada de propósito SEM administrador** | Critério de aceite 5: tem que rodar, marcar as etapas privilegiadas como "Ignorado — sem privilégio" e ainda gerar relatório útil |

As outras quatro podem ser o que aparecer na frente — de preferência máquinas de perfis diferentes entre si. Comece pelas máquinas internas da Epicora: é a primeira execução de um binário que nunca rodou, e o lugar de descobrir isso não é na frente de um cliente.

---

## O que anotar em cada máquina

Isto é o produto do pré-voo. Um relatório bonito que ninguém conferiu não vale nada — o valor está no que **não** bateu com a realidade.

1. **Quanto tempo levou** a coleta (a tela 2 mostra).
2. **Algo em "não foi possível verificar"** que a máquina claramente tem? (ex.: aparece sem TPM, e a máquina tem TPM)
3. **Algum valor errado?** Modelo ou tamanho do disco, quantidade de memória, antivírus instalado, edição do Windows. Este é o achado mais importante que o pré-voo pode produzir.
4. **Algum achado com que você discorda?** Marque como falso positivo na tela 3, com o motivo.
5. **A janela travou ou congelou** em algum momento?
6. **SmartScreen, antivírus ou EDR atrapalharam?** Qual produto, qual mensagem.
7. **Texto cortado ou fonte errada?** O cabeçalho roxo, o título de cada tela e o número grande do score usam a tipografia da Epicora, e **esta é a primeira vez que ela roda em Windows**. Se algum título aparecer em Arial, ou se o texto de um cartão da tela 3 terminar cortado no fim da última linha, é isso — mande uma captura de tela. Vale conferir também em máquina com escala de 125% ou 150%, que é a de fábrica em boa parte dos notebooks.

Não precisa formatar nada: um bloco de texto por máquina, junto do ZIP, resolve.

---

## O que não fazer

- **Não entregar o relatório ao cliente** nesta rodada. A lista de riscos está curta de propósito — 23 regras de 61 —, e revisão no escritório vem antes.
- **Não renomear o executável.** O nome fixo é o que faz o link estável funcionar, e o hash publicado é do arquivo com esse nome.
- **Não rodar em servidor.** Fora de escopo declarado (doc 01 §10).
- **Não mexer nos arquivos de saída** antes de enviar, nem no log.
- **Não desativar proteção nenhuma** da máquina do cliente para fazer a ferramenta rodar.

---

## Mensagem pronta para enviar ao técnico

> Preciso da sua ajuda para testar a ferramenta de diagnóstico em algumas máquinas. Ela **só lê** — não altera nada, não instala nada, não envia nada para servidor.
>
> **1.** Baixe na própria máquina:
> https://github.com/Epicora-Software-House/epicora-checkup/releases/latest/download/EpicoraCheckup.exe
>
> **2.** Se o navegador reclamar que o arquivo é incomum, escolha *Manter*. Se o Windows disser que "protegeu seu PC", clique em *Mais informações* → *Executar assim mesmo*. **Os dois avisos são esperados**: o programa é nosso e não está assinado.
>
> **3.** Botão direito → *Executar como administrador*, se você tiver a senha. Sem ela roda também, só sai um pouco menos completo.
>
> **4.** Preencha a primeira tela, clique em *Iniciar coleta* e não feche a janela (leva menos de 1 minuto e meio).
>
> **5.** Na última tela tem um botão que abre a pasta com os arquivos. **Compacte a pasta `EpicoraCheckup` e me manda.**
>
> **6.** E me manda por escrito: quanto tempo levou, se apareceu algum dado errado (disco, memória, antivírus), se travou, e se algum antivírus bloqueou. **É isso que estou testando** — achar algo errado é o resultado útil, não o problema.
>
> Se o antivírus bloquear: **não desative nada**, só me avisa qual produto foi.
