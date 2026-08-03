using System.Threading;

namespace EpicoraCheckup.Core.Contracts
{
    /// <summary>
    /// Contrato único de coleta, um por domínio (doc 02 §3.1). Dezesseis implementações
    /// previstas, listadas em schema/campo-para-decisao.md.
    ///
    /// O protótipo PowerShell em tools/prototype/ implementa o mesmo contrato, função a
    /// função, de propósito: a Fase 2 é porte, não reescrita, e o .ps1 segue vivo como
    /// fallback permanente (ADR-009).
    /// </summary>
    public interface ICollector
    {
        /// <summary>Identificador estável, usado como chave no JSON: "storage", "security".</summary>
        string Id { get; }

        /// <summary>Rótulo exibido na lista de etapas da tela 2.</summary>
        string DisplayName { get; }

        /// <summary>
        /// Se a coleta exige privilégio administrativo.
        ///
        /// A sonda de campo mediu que isso é mais raro do que o documento supunha: só
        /// TPM, BitLocker e SMART exigem elevação, e as três degradam para null
        /// isoladamente. Um coletor inteiro só deve declarar <c>true</c> aqui quando
        /// NADA nele responde sem privilégio — caso contrário se descarta de graça a
        /// família de achados comercialmente mais valiosa em toda visita sem senha de
        /// administrador.
        /// </summary>
        bool RequiresElevation { get; }

        /// <summary>Só alimenta a barra de progresso. Não é timeout nem contrato.</summary>
        int EstimatedSeconds { get; }

        /// <summary>
        /// Executa a coleta. Não deve lançar: o orquestrador envolve cada chamada em
        /// try/catch individual porque falha de um coletor nunca pode abortar os
        /// outros, mas um coletor que trata os próprios erros produz resultado parcial
        /// útil em vez de nada.
        /// </summary>
        CollectorResult Collect(CollectionContext context, CancellationToken cancellationToken);
    }
}
