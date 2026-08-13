# ADR-015 — Como a matriz de regras é versionada

**Estado:** ✅ Aceita
**Data:** 2026-08-13
**Decisão de:** Gabriel Oss
**Referência:** doc 02 §5 (schema, campo `tool.rulesVersion`) e §8.5 (versão gravada na saída); doc 03 §1 (matriz); [ADR-013](013-executavel-unico.md)

## Contexto

O schema 1.0 tem `tool.rulesVersion` desde o começo, e ele sai **nulo** desde o começo — no C# e no protótipo. As fixtures sintéticas trazem `"2026.07.29"`, uma data escrita à mão que não vem de lugar nenhum.

O campo existe para responder uma pergunta específica, que aparece meses depois da visita: **qual matriz produziu este número.** Ela deixou de ser hipotética com o [ADR-013](013-executavel-unico.md): uma pasta `rules/` ao lado do executável tem precedência sobre a matriz embutida, então duas máquinas podem rodar o mesmo `EpicoraCheckup.exe` com critérios diferentes. O log registra a origem; o arquivo de saída, que é o que sobrevive e o que o consolidador lê, não registrava nada.

## Decisão

**`tool.rulesVersion` é a data declarada da revisão, mais a impressão digital do conteúdo carregado:**

```
2026.08.12+9f3c1ab2
```

| Parte | De onde vem | O que é |
|---|---|---|
| `2026.08.12` | campo `version` de `rules/matriz.json` | **Rótulo**, escolhido por quem revisou a matriz |
| `9f3c1ab2` | SHA-256 do conteúdo dos arquivos de regra carregados, 4 primeiros bytes | **Fato**, e não depende de ninguém lembrar de nada |

Regras da segunda parte, todas com teste:

- Sobre os arquivos de **regra**, na ordem de carga — ordinal por nome. Nome de arquivo entra no material do hash, porque mover uma regra de arquivo muda a ordem de carga, e a ordem de carga é parte da saída (`Score.VerdictDrivenBy` a preserva).
- **Não** sobre os arquivos de apoio (`event-ids`, `windows-builds`, `win11-cpu-support`, `startup-exclusions`): eles alimentam coletor, não matriz. Mudam o que a máquina responde, não o critério de avaliação.
- Fim de linha normalizado para `\n` antes do hash.
- Sem declaração, sobra a impressão digital sozinha. Declaração ilegível não derruba nada.

## Por que as duas partes, e não uma

**Só a data mente.** Alguém edita um peso, esquece de bumpar, e o relatório passa a afirmar uma matriz que não é a que rodou. O campo existe justamente para ser confiável no dia em que um cliente contesta um achado — um campo que às vezes mente é pior que campo nulo, porque nulo ninguém usa como prova.

**Só a impressão digital não ordena.** `9f3c1ab2` é mais novo que `4e21bb70`? Impossível dizer sem consultar o repositório. Dois relatórios lado a lado na mesa de uma reunião precisam ser comparáveis por quem está na sala.

Juntas: a data é lida por gente, a impressão digital é conferida por quem precisa de prova. É como versão de software funciona há décadas — o número é declarado, o commit é o fato.

## Por que o fim de linha é normalizado

Sem isso, alguém abre `storage.json` no Notepad de uma máquina Windows, salva sem alterar nada, e a matriz passa a alegar outra versão. Na primeira vez que isso acontecesse, ninguém confiaria mais no campo — e com razão, porque ele estaria reportando mudança onde não houve nenhuma.

O nome do arquivo, ao contrário, entra no hash de propósito: renomear muda comportamento de verdade.

## Por que 4 bytes

Isto identifica revisão de matriz para auditoria interna. Não protege contra ninguém forjando colisão — quem tem acesso para editar a matriz tem acesso para editar o número de versão declarado, e o repositório é público de todo modo (ADR-002). Oito dígitos hexadecimais caber num rodapé de relatório vale mais que resistência a ataque que não existe neste modelo.

## Alternativas consideradas

**Só a data, como as fixtures já faziam.** É o formato mais simples e o único que o protótipo PowerShell poderia produzir sem esforço. Recusada pelo motivo acima: mente em silêncio.

**Número de versão semântico da matriz, bumpado à mão.** Mesmo defeito da data, com o custo extra de ninguém saber o que é uma mudança "maior" numa matriz de risco.

**Máximo dos campos `version` das regras individuais.** Não detecta alteração de peso, condição ou texto sem bump — que é exatamente o caso que preocupa.

**Hash da forma canônica das regras já interpretadas**, em vez do texto dos arquivos. Mais preciso conceitualmente: ignoraria reformatação de JSON. Recusada por custo/benefício — exigiria serialização canônica estável, que é código sutil, e reformatar arquivo de matriz não é operação que aconteça sem alguém mexer nele de propósito.

## Consequências

1. **`rules/matriz.json` passa a existir**, na lista de arquivos de apoio do `RuleRepository` e do `tools/validate-rules.mjs`. Não contém regras; se saísse dessa lista, o carregamento falharia por não ter a lista `rules`.

2. **A data declarada é validada no CI** — formato `AAAA.MM.DD`, data existente, não no futuro. Errar o rótulo não quebra execução nenhuma: sai torto no relatório do cliente, que é onde ninguém quer descobrir.

3. **Bumpar a data é procedimento manual, e esquecer não é catástrofe.** Sai uma data velha ao lado de uma impressão digital nova, o que é legível e não é falso. Está dito dentro do próprio `matriz.json`.

4. **O protótipo PowerShell continua com `rulesVersion` nulo.** Ele coleta e não avalia — não carrega matriz nenhuma, então não tem versão de matriz a declarar. Nulo é a resposta correta, e não uma pendência ([ADR-009](009-prototipo-powershell-e-fallback-permanente.md)).

5. **As três fixtures sintéticas seguem com `"2026.07.29"`.** São entrada de teste, e o campo do documento produzido é recalculado a cada execução — reescrevê-las mudaria golden file por motivo cosmético.

## Revisão

Reabrir se a matriz passar a ser distribuída separada do executável — por exemplo baixada de uma URL —, caso em que a impressão digital deixa de ser derivável do que veio embutido e passa a precisar viajar junto do arquivo.
