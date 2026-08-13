using System;
using System.Reflection;

namespace EpicoraCheckup.App
{
    /// <summary>
    /// Que build é este: número de versão e commit, lidos do próprio assembly.
    ///
    /// Os dois vão para o bloco <c>tool</c> do arquivo de saída (doc 02 §8.5) e a versão
    /// também alimenta a verificação de versão da tela 1 — daí não viverem dentro do
    /// <see cref="ReportInputFactory"/>, que é sobre montar relatório.
    ///
    /// **Quem carimba é o CI**, e não este código: <c>-p:Version</c> vem da tag <c>v*</c> e
    /// <c>-p:SourceRevisionId</c> vem do SHA do commit. Num build local os dois saem no
    /// padrão — versão de desenvolvimento e commit nulo —, e isso é a resposta certa: um
    /// relatório produzido por binário de bancada não deve alegar procedência que não tem.
    /// </summary>
    internal static class ToolIdentity
    {
        /// <summary>
        /// Versão no formato N.N.N que o schema exige. Sem isso é impossível auditar qual
        /// versão produziu qual relatório, e isso vai importar no primeiro relatório
        /// contestado por um cliente (doc 02 §8.5).
        /// </summary>
        internal static string Version
        {
            get
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;

                return version == null
                    ? "0.1.0"
                    : $"{version.Major}.{version.Minor}.{version.Build}";
            }
        }

        /// <summary>
        /// Commit que originou este binário, ou <c>null</c> em build sem carimbo.
        ///
        /// Vem do sufixo de metadados do <c>AssemblyInformationalVersion</c> —
        /// <c>1.0.0+4fbdd76</c> —, que é onde o SDK deposita o <c>SourceRevisionId</c>
        /// passado pelo CI. <c>AssemblyVersion</c> não serve: só aceita números.
        ///
        /// Nulo em vez de "desconhecido" ou string vazia: campo ausente é <c>null</c> no
        /// schema 1.0, e um placeholder sobreviveria a um grep procurando relatório sem
        /// procedência.
        /// </summary>
        internal static string Commit
        {
            get
            {
                var informational = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

                var text = informational == null ? null : informational.InformationalVersion;

                if (string.IsNullOrWhiteSpace(text)) return null;

                var plus = text.IndexOf('+');

                if (plus < 0 || plus == text.Length - 1) return null;

                var commit = text.Substring(plus + 1).Trim();

                return commit.Length == 0 ? null : commit;
            }
        }
    }
}
