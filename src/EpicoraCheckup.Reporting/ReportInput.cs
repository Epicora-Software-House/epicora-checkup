using System;
using System.Collections.Generic;
using EpicoraCheckup.Core.Contracts;
using EpicoraCheckup.Core.Model;

namespace EpicoraCheckup.Reporting
{
    /// <summary>
    /// Tudo o que é preciso para montar o documento de saída.
    ///
    /// Objeto de entrada explícito, em vez de o Reporting conhecer o estado da aplicação:
    /// é o que permite o consolidador da Fase 4 e os testes montarem um documento sem
    /// instanciar tela nenhuma.
    /// </summary>
    public sealed class ReportInput
    {
        public Identification Identification { get; set; } = new Identification();

        public ManualData Manual { get; set; } = new ManualData();

        public bool IsElevated { get; set; }

        public DateTimeOffset StartedAt { get; set; }

        public DateTimeOffset FinishedAt { get; set; }

        public IList<CollectorResult> Collectors { get; set; } = new List<CollectorResult>();

        /// <summary>
        /// Nulos antes da avaliação. O documento montado sem eles serve de ENTRADA para o
        /// motor de regras — que precisa do bloco <c>manual</c>, porque OS-004 lê
        /// <c>manual.corporateEnvironment</c>.
        /// </summary>
        public IList<Finding> Findings { get; set; }

        public Score Score { get; set; }

        /// <summary>Versão da ferramenta, no formato N.N.N que o schema exige.</summary>
        public string ToolVersion { get; set; } = "0.1.0";

        public string Commit { get; set; }

        /// <summary>Versão da matriz de regras, para auditar um relatório contestado.</summary>
        public string RulesVersion { get; set; }

        public string HostLocale { get; set; }
    }
}
