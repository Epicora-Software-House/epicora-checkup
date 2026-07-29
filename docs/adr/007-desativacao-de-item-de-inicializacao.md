# ADR-007 — Mecanismo de desativação de item de inicialização

**Estado:** Aceita
**Data:** 2026-07-29
**Aplica-se a:** Fase 5 (OPT-STARTUP)
**Referência:** doc técnico §4.8 e §11 ponto 7; doc 03 §5.2

## Contexto

Duas formas de desativar um item de inicialização:

**A. Escrever em `...\Explorer\StartupApproved\Run`.** É o que o Gerenciador de Tarefas usa. O formato é **binário e não documentado** — marcado confiança B no documento técnico. Escrever formato errado pode corromper o estado de inicialização da máquina do cliente.

**B. Mover a entrada da chave `Run` para uma chave de backup própria da Epicora.** O item deixa de iniciar porque não está mais em `Run`. Totalmente reversível e documentado por nós mesmos.

## Decisão

**Opção B.** Mover a entrada para:

```
HKLM\SOFTWARE\Epicora\Checkup\StartupBackup
HKCU\SOFTWARE\Epicora\Checkup\StartupBackup
```

preservando nome do valor, tipo e dado originais, mais um valor irmão com a origem (`HKLM` ou `HKCU`, `Run` ou `RunOnce`), timestamp e o identificador do diagnóstico.

**Nunca escrever em `StartupApproved`.** Leitura para diagnóstico é permitida; escrita, não.

## Consequências

- **Reversão é trivial e nossa:** mover o valor de volta. Não depende de entender formato de terceiro.
- **O Gerenciador de Tarefas não vai mostrar o item como "Desabilitado"** — ele simplesmente some da lista. Isso precisa constar no relatório e no procedimento do técnico, senão o TI do cliente estranha.
- Cria uma chave de registro sob `HKLM\SOFTWARE\Epicora`. É a **única** escrita persistente que a ferramenta faz em qualquer versão, e existe exclusivamente para permitir desfazer. Não é mecanismo de inicialização nem de persistência da própria ferramenta — a proibição do documento funcional §7.1 continua valendo integralmente.

## Controles obrigatórios que acompanham (doc 03 §5.2)

Nenhum é opcional:

1. Lista de exclusão por nome de processo e por fabricante em `rules/startup-exclusions.json`. Itens da lista **aparecem na tela, com marcação de bloqueio, e não podem ser selecionados.**
2. Item de fabricante desconhecido ou sem assinatura digital exige confirmação adicional em diálogo separado.
3. Valor original gravado no log **antes** da alteração.
4. Marcação individual obrigatória. Sem seleção em lote, em nenhuma circunstância.

## Escopo

Cobre as chaves `Run` e `RunOnce` de `HKLM` e `HKCU`. **Não** cobre pastas de Inicialização, tarefas agendadas com gatilho de logon, nem serviços — esses são reportados no inventário mas não são desativáveis pela ferramenta. Mexer em serviços em bloco está na lista negra do documento funcional §7.2.
