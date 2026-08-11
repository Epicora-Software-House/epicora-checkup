using System.Globalization;
using System.Reflection;
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

                ToolVersion = ToolVersion,
                HostLocale = CultureInfo.CurrentCulture.Name
            };
        }

        /// <summary>
        /// Versão da ferramenta no formato N.N.N que o schema exige. Sem isso é impossível
        /// auditar qual versão produziu qual relatório, e isso vai importar no primeiro
        /// relatório contestado por um cliente (doc 02 §8.5).
        /// </summary>
        private static string ToolVersion
        {
            get
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                return version == null
                    ? "0.1.0"
                    : $"{version.Major}.{version.Minor}.{version.Build}";
            }
        }
    }
}
