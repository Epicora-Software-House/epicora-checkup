# ADR-003 — Certificado de assinatura de código

**Estado:** ⏸ **Pendente — decisão da direção + levantamento de preço**
**Data de abertura:** 2026-07-29
**Prazo:** antes do início da Fase 3
**Bloqueia:** Fase 3 (etapa de assinatura no CI)
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

## Levantamento necessário antes de decidir

Preço e requisitos de emissão mudam com frequência. Levantar com autoridade certificadora, na Fase 0:

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

_A preencher._

- Escolha:
- Quem decidiu:
- Data:
