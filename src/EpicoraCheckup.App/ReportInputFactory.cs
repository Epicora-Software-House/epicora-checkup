using System.Globalization;
using EpicoraCheckup.Reporting;

namespace EpicoraCheckup.App
{
    /// <summary>
    /// Traduz o estado da sessão para a entrada do Reporting.
    ///
    /// Existe para que o mesmo documento seja montado nos dois momentos em que é preciso —
    /// como entrada do motor de regras na tela 2, e como arquivo de saída na tela 7. Montar
    /// de formas diferentes produziria regra que dispara na ferramenta e não aparece no
    /// relatório.
    /// </summary>
    internal static class ReportInputFactory
    {
        internal static ReportInput From(SessionState session, bool withEvaluation)
        {
            return new ReportInput
            {
                Identification = session.Identification,
                Manual = session.Manual,
                IsElevated = session.IsElevated,
                StartedAt = session.StartedAt,
                FinishedAt = session.FinishedAt ?? session.StartedAt,
                Collectors = session.CollectorResults,

                // Nulos antes da avaliação: o documento da tela 2 é ENTRADA do motor, e
                // preenchê-los ali com placeholder faria o motor ler o próprio resultado.
                Findings = withEvaluation ? session.Findings : null,
                Score = withEvaluation ? session.Score : null,

                ToolVersion = ToolIdentity.Version,
                Commit = ToolIdentity.Commit,

                // Preenchida na tela 2, quando a matriz é carregada. Nula em documento
                // montado antes disso — o que não acontece no fluxo, mas é honesto.
                RulesVersion = session.RulesVersion,

                HostLocale = CultureInfo.CurrentCulture.Name
            };
        }
    }
}
