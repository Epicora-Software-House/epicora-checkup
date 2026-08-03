namespace EpicoraCheckup.Core.Orchestration
{
    /// <summary>
    /// Os cinco estados que a tela 2 mostra por etapa (doc 01 §5).
    ///
    /// Diferente de <see cref="Model.CollectorStatus"/>: aquele é o estado final que vai
    /// para o JSON, este inclui os estados transitórios da interface.
    /// </summary>
    public enum CollectorPhase
    {
        Pending,
        Running,
        Completed,
        Skipped,
        Failed
    }

    /// <summary>
    /// Um evento de progresso da coleta, para a tela 2.
    ///
    /// Instância imutável por evento, de propósito: o orquestrador roda fora da thread da
    /// UI, e reaproveitar um objeto mutável entre notificações produz corrida em que a tela
    /// lê o estado da etapa seguinte.
    /// </summary>
    public sealed class CollectionProgress
    {
        public string CollectorId { get; set; }

        public string DisplayName { get; set; }

        /// <summary>Base zero.</summary>
        public int Index { get; set; }

        public int Total { get; set; }

        public CollectorPhase Phase { get; set; }

        /// <summary>Resumo de uma linha, quando a etapa termina bem.</summary>
        public string Summary { get; set; }

        /// <summary>Motivo do ignorado ou do erro, em uma linha, para exibir.</summary>
        public string Detail { get; set; }

        public long DurationMs { get; set; }

        public bool TimedOut { get; set; }
    }
}
