# ADR-003 — Certificado de assinatura de código

**Estado:** ✅ **Aceita — nenhum certificado na v1**
**Data de abertura:** 2026-07-29
**Data da decisão:** 2026-07-29
**Bloqueia:** nada. A Fase 3 sai sem etapa de assinatura no CI
**Referência:** doc técnico §8.4 e §11 ponto 3

## Contexto

O perfil comportamental da ferramenta é exatamente o que um EDR moderno bloqueia: executável desconhecido, baixado da internet, executado com elevação, varrendo hardware, lendo o registro e — na Fase 5 — apagando arquivos. Somado a isso, arquivo baixado por navegador recebe marca de zona (Mark-of-the-Web), o que aciona o aviso de aplicativo não reconhecido do SmartScreen.

**Isso vai acontecer. Não é hipótese.** A questão é quanto reduzir.

## Opções

| Opção | Efeito | Custo |
|---|---|---|
| **Certificado EV** | Reputação praticamente imediata no SmartScreen | Mais caro; hoje exige armazenamento em token físico ou HSM, o que complica assinatura automatizada no CI |
| **Certificado OV** | Reduz o problema, mas precisa acumular reputação — leva tempo e volume de downloads | Mais barato |
| **Nenhum na v1** | Zero custo | Aviso de SmartScreen em toda máquina, toda vez. Atrito direto na frente do cliente |

## Levantamento adiado

O levantamento abaixo **não foi feito** e não é pré-requisito de nenhuma fase. Fica registrado para quando a decisão for revisitada:

- [ ] Preço anual OV e EV
- [ ] Requisito atual de armazenamento de chave (token físico, HSM, serviço de assinatura em nuvem)
- [ ] Se há serviço de assinatura em nuvem compatível com GitHub Actions — isso é o que decide se EV é viável no CI
- [ ] Documentação exigida para validar a Epicora como organização, e prazo de emissão

## Mitigações que valem independentemente da escolha

Estas entram na Fase 3 mesmo sem certificado:

1. Hash SHA-256 publicado ao lado do binário, em `SHA256SUMS`.
2. **Procedimento documentado de exceção** para o técnico: como pedir ao responsável de TI do cliente uma exclusão temporária, e como proceder se ele negar.
3. **Plano de contingência obrigatório:** se o executável for bloqueado e não houver como liberar, o técnico usa o script PowerShell. Ver [ADR-009](009-prototipo-powershell-e-fallback-permanente.md).

## Decisão registrada

- **Escolha:** nenhum certificado na v1. O binário sai sem assinatura.
- **Quem decidiu:** Gabriel Oss
- **Data:** 2026-07-29

## Consequências assumidas

Isto não é postergar a decisão, é escolher a terceira opção da tabela com os olhos abertos:

1. **SmartScreen avisa em toda máquina, toda vez.** O técnico precisa saber disso antes de chegar no cliente, não descobrir na frente dele. Entra no procedimento de campo da Fase 3.
2. **O EDR do cliente bloqueia com mais frequência** do que bloquearia um binário assinado. Isso **eleva** o fallback PowerShell de contingência a caminho rotineiro — que é exatamente o que a [ADR-009](009-prototipo-powershell-e-fallback-permanente.md) previu. Reforça a decisão de manter o `.ps1` legível e num arquivo único: é ele que salva a visita.
3. As três mitigações da seção acima passam de "valem independentemente" a **obrigatórias** na Fase 3, em especial o `SHA256SUMS` e o procedimento documentado de exceção.

## Quando revisitar

Não há prazo. Os gatilhos são:

- atrito de SmartScreen ou bloqueio de EDR virar reclamação recorrente de cliente ou perda de visita;
- o parque atendido crescer ao ponto de o custo do certificado ficar abaixo do custo do atrito;
- exigência contratual de cliente corporativo por binário assinado.
