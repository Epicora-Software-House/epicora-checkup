# ADR-016 — Identidade visual da Epicora no executável e no relatório

**Estado:** ✅ Aceita
**Data:** 2026-08-14
**Contexto:** Fase 3 fechada, pré-voo em andamento

## Contexto

A ferramenta é operada na frente do cliente e produz um relatório que fica com ele. Até aqui
a aparência era neutra: cinza, branco e Segoe UI. Neutro não é errado, mas é uma oportunidade
perdida — o técnico passa a visita inteira com a janela aberta na mesa do cliente, e o
relatório circula depois dela.

A fonte de verdade da marca é o **Manual de Marca da Epicora** e os decks comerciais que já a
aplicam (`administrare/vendere/orçamentos/*.html`), que fixaram na prática o que o manual
descreve: preto, roxo e branco, com verde-água só como indicador positivo.

| Elemento | Valor |
|---|---|
| Roxo (isotipo, cor-mãe) | `#6100FF` |
| Roxo profundo / lilás | `#2A0A78` / `#A98BFF` |
| Ink / grafite | `#08080A` / `#141418` |
| Verde-água | `#14FFB9` |
| Vermelho / amarelo neon | `#FD4950` / `#DAFF19` |
| Tipografia corrida | Alexandria (SIL OFL 1.1) |
| Tipografia de título | "Cy" no manual; os decks usam Space Grotesk |

## Decisões

### 1. Cromo na marca, semáforo semântico

O roxo manda no cabeçalho, na ação principal e no link. As cores de severidade e de faixa
**não** mudam.

O verde-água `#14FFB9` sobre branco tem contraste perto de 1,5:1 e o amarelo neon é pior. O
semáforo é lido em projetor de sala de reunião e em tela com escala de 150%, e ali quem manda
é contraste. Trocar as cores do semáforo pelas da paleta tornaria o entregável mais bonito em
captura de tela e ilegível na situação em que ele é usado.

### 2. Corpo claro, e não o escuro dos decks

Os decks são `#08080A` com glow roxo. O executável fica claro.

WinForms em .NET Framework não tem tema escuro: `TextBox`, barra de rolagem e controle
desabilitado são desenhados pelo Windows e ignoram `BackColor`. Um escuro convincente exigiria
redesenhar controle nativo à mão, e as telas 1 e 4 são formulários. O custo cairia inteiro
sobre a parte da ferramenta que menos precisa de marca.

### 3. A faixa de demonstração deixa de ser roxa

Era `#782080`. Com o cabeçalho roxo, a faixa que existe para gritar "isto não é real" passaria
a ter aproximadamente a cor do cromo normal. Passa a ser amarelo neon `#DAFF19` sobre ink —
combinação de sinalização, e a única da paleta que não colide nem com o semáforo nem com o
cabeçalho.

### 4. Alexandria em título, Segoe UI em corpo

Dois cortes estáticos (Regular e SemiBold), subconjunto latin + latin-ext, ~30 KB cada,
embutidos no executável.

O manual pede Alexandria no texto corrido. O corrido desta ferramenta é o `clientText` da tela
3: parágrafo longo, em 9,75 pt, lido por cima do ombro do técnico. Nesse tamanho o Segoe UI foi
desenhado para a tela do Windows e a Alexandria não. O outro motivo é de risco: o corrido vive
em painel de altura fixa e em cartão medido com `TextRenderer`, e métrica diferente ali corta
texto do cliente.

Três detalhes que não são óbvios:

- **Estática, não variável.** O GDI não interpola eixo de peso; carregar a variável do kit de
  marca entregaria sempre a instância padrão.
- **Duas famílias, não dois cortes da mesma.** Pedir `FontStyle.Bold` a uma família privada que
  não tem o corte lança `ArgumentException`. Com `Alexandria` e `Alexandria SemiBold` separadas,
  todo `Font` pedido é Regular e nada é sintetizado pelo GDI+.
- **`AddMemoryFont` e `AddFontMemResourceEx`, os dois.** O primeiro atende o GDI+, que desenha;
  o segundo atende o GDI, que é quem o `TextRenderer` da tela 3 usa para **medir**. Registrar
  só num faz medida e desenho usarem fontes diferentes.

Fallback é **Arial**, que é o que o próprio manual manda usar quando a Alexandria não é
possível. Falha de carga registra o motivo no log e não impede nada.

### 5. "Cy" fica de fora

O manual pede Cy nos títulos. O arquivo não está na pasta `Fontes` do kit e os decks já a
substituíram por Space Grotesk. Aqui os títulos usam Alexandria SemiBold, que está licenciada e
disponível. Se a direção quiser Cy, é preciso o arquivo e a licença — e vira revisão deste ADR.

### 6. No relatório, logotipo roxo sobre branco

O executável usa o logotipo branco sobre a faixa roxa. O relatório HTML usa o **roxo sobre
branco**, como `<img>` e não como `background-image`.

Navegador imprime com "gráficos de fundo" desligado por padrão. Uma faixa roxa sairia branca no
papel, com o logotipo branco em cima dela — invisível. Sobre branco, imprime igual na tela e no
papel, e o doc 02 §6 exige que a impressão em A4 funcione.

### 7. A licença da fonte viaja junto

A SIL OFL 1.1 exige que a licença acompanhe cada cópia do software que redistribui a fonte. O
`.exe` embute `OFL.txt`; o relatório HTML embute o WOFF2 e leva o texto da licença num bloco
recolhido no rodapé, escondido na impressão — no papel não há fonte embutida, logo não há
redistribuição a licenciar.

Isso obrigou a **precisar** a verificação de autocontenção do relatório: o teste procurava a
string `http://`, e o texto da OFL cita a URL da própria licença. Passou a procurar
`src=`, `href=` e `url()` apontando para fora, que é a propriedade real — o relatório não busca
nada na rede. A verificação ficou mais estreita, não mais frouxa.

## Consequências

- O executável vai de ~1,0 MB para ~1,2 MB. Continua um arquivo só (ADR-013).
- Cada relatório HTML cresce ~25 KB: o WOFF2 e o logotipo em data URI, mais a licença.
- O `.exe` passa a ter ícone próprio — o isotipo roxo — visível no Explorer e na barra de
  tarefas antes mesmo da primeira execução.
- **Nada disto foi visto rodando.** Vale para o WinForms o mesmo que vale para o resto do porte:
  compila, mas nenhuma linha desenhou pixel em Windows. O relatório HTML foi renderizado e
  impresso de verdade; a janela, não. Entra no roteiro de pré-voo.
