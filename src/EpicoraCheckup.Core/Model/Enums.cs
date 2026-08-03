namespace EpicoraCheckup.Core.Model
{
    /// <summary>
    /// Severidade de um achado. A ordem dos membros É a ordem de apresentação no
    /// relatório e a ordem de ordenação dos achados — não reordenar sem olhar
    /// RuleEngine.SortFindings.
    /// </summary>
    public enum Severity
    {
        Critical = 0,
        High = 1,
        Medium = 2,
        Low = 3,
        Info = 4
    }

    /// <summary>
    /// Os três estados de uma regra. <see cref="Indeterminate"/> não é detalhe de
    /// implementação: é o princípio 3 do documento funcional. Falha de coleta nunca
    /// vira achado negativo, e <see cref="Indeterminate"/> não pontua no score.
    /// </summary>
    public enum RuleState
    {
        Compliant,
        NonCompliant,
        Indeterminate
    }

    /// <summary>
    /// Estado de um coletor ao fim da execução. Qualquer valor diferente de
    /// <see cref="Completed"/> faz as regras que dependem dele resolverem
    /// <see cref="RuleState.Indeterminate"/>.
    /// </summary>
    public enum CollectorStatus
    {
        Completed,
        Skipped,
        Failed
    }

    /// <summary>Faixa do semáforo da tela 3.</summary>
    public enum ScoreBand
    {
        Green,
        Yellow,
        Red
    }

    /// <summary>Veredito comercial da máquina.</summary>
    public enum Verdict
    {
        Keep,
        Upgrade,
        Replace
    }
}
