using System;
using System.Collections.Generic;
using EpicoraCheckup.Core.Contracts;
using EpicoraCheckup.Core.Model;

namespace EpicoraCheckup.Reporting
{
    /// <summary>
    /// Tudo o que uma execução produziu, na forma que a gravação precisa.
    ///
    /// É um tipo próprio, e não o estado da tela: Reporting não conhece WinForms nem o
    /// assistente, e o consolidador da Fase 4 vai montar isto a partir de arquivo lido do
    /// disco, sem UI nenhuma no caminho.
    /// </summary>
    public sealed class CheckupRun
    {
        public string ToolVersion { get; set; }

        /// <summary>Commit que gerou o binário, quando o CI informa. Nulo em build local.</summary>
        public string Commit { get; set; }

        public string RulesVersion { get; set; }

        public DateTimeOffset StartedAt { get; set; }

        public DateTimeOffset FinishedAt { get; set; }

        public bool Elevated { get; set; }

        public string Technician { get; set; }

        public string DiagnosticId { get; set; }

        public string HostLocale { get; set; }

        public string ClientName { get; set; }

        public string ClientUnit { get; set; }

        public string MachineLabel { get; set; }

        public string Responsible { get; set; }

        public string Department { get; set; }

        public string PhysicalLocation { get; set; }

        public string AssetTag { get; set; }

        public string PhysicalCondition { get; set; }

        public string Notes { get; set; }

        /// <summary>
        /// Marcação do técnico. Habilita OS-004 quando a máquina não está em domínio mas o
        /// ambiente é corporativo.
        /// </summary>
        public bool? CorporateEnvironment { get; set; }

        public IList<CollectorResult> Collectors { get; set; } = new List<CollectorResult>();

        public IList<Finding> Findings { get; set; } = new List<Finding>();

        public Score Score { get; set; }

        public int DurationSeconds
        {
            get { return Math.Max(0, (int)(FinishedAt - StartedAt).TotalSeconds); }
        }
    }
}
