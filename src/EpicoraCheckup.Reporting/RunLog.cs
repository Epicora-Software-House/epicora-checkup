using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EpicoraCheckup.Core.Contracts;
using EpicoraCheckup.Core.Model;

namespace EpicoraCheckup.Reporting
{
    /// <summary>
    /// Log da execução — o terceiro arquivo da saída (doc 01 §8).
    ///
    /// É o que se lê quando um relatório é contestado: por que uma etapa foi ignorada, quanto
    /// tempo cada uma levou, qual fonte falhou e com que mensagem. O <c>Detail</c> dos erros
    /// entra AQUI e em nenhum outro lugar — o relatório HTML mostra "não foi possível
    /// verificar" em linguagem de cliente, e o nome de uma classe WMI não é isso.
    /// </summary>
    public static class RunLog
    {
        public static string Build(CheckupRun run, IEnumerable<string> avisos = null)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));

            var log = new StringBuilder();

            Line(log, run.StartedAt, "INFO", string.Format(
                "Epicora Checkup {0} · runtime dotnet · schema {1}",
                CheckupDocument.Version(run.ToolVersion), CheckupDocument.SchemaVersion));

            Line(log, run.StartedAt, "INFO", "diagnóstico " + run.DiagnosticId + " · técnico " + run.Technician);
            Line(log, run.StartedAt, "INFO", "cliente " + run.ClientName);
            Line(log, run.StartedAt, "INFO", run.Elevated
                ? "elevado: SIM"
                : "elevado: NÃO — TPM, BitLocker e SMART não serão lidos");

            foreach (var collector in run.Collectors)
            {
                if (collector == null) continue;

                Line(log, run.StartedAt, Level(collector), string.Format(
                    "coletor {0} terminou em {1} ms com status {2}{3}",
                    collector.Id, collector.DurationMs, collector.Status,
                    collector.Status == CollectorStatus.Skipped && collector.SkipReason != null
                        ? " — " + collector.SkipReason
                        : string.Empty));

                if (collector.Errors == null) continue;

                foreach (var error in collector.Errors)
                {
                    Line(log, run.StartedAt, "WARN", string.Format(
                        "  {0} · {1} · {2}", collector.Id, error.Source, error.Message));

                    if (!string.IsNullOrWhiteSpace(error.Detail))
                        Line(log, run.StartedAt, "DEBUG", "  " + OneLine(error.Detail));
                }
            }

            Line(log, run.FinishedAt, "INFO", string.Format(
                "avaliação: {0} achados, índice {1}",
                run.Findings == null ? 0 : run.Findings.Count,
                run.Score == null ? "não calculado" : run.Score.Value.ToString(CultureInfo.InvariantCulture)));

            if (avisos != null)
                foreach (var aviso in avisos) Line(log, run.FinishedAt, "WARN", aviso);

            Line(log, run.FinishedAt, "INFO", string.Format(
                "concluído em {0} s", run.DurationSeconds));

            return log.ToString();
        }

        private static string Level(CollectorResult collector)
        {
            if (collector.Status == CollectorStatus.Failed) return "ERROR";
            if (collector.Status == CollectorStatus.Skipped) return "WARN";

            return "INFO";
        }

        /// <summary>
        /// Pilha de exceção em várias linhas dentro de um log de uma linha por evento vira
        /// texto impossível de filtrar com <c>findstr</c>, que é a ferramenta que existe na
        /// máquina do cliente.
        /// </summary>
        private static string OneLine(string detail)
        {
            return detail.Replace("\r\n", " | ").Replace("\n", " | ").Replace("\r", " | ");
        }

        private static void Line(StringBuilder log, DateTimeOffset moment, string level, string message)
        {
            log.Append(moment.ToString("o", CultureInfo.InvariantCulture));
            log.Append(" [").Append(level.PadRight(5)).Append("] ");
            log.Append(message);
            log.Append("\r\n");
        }
    }
}
