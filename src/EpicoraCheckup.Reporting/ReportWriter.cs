using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Reporting
{
    /// <summary>
    /// Grava o pacote de saída de uma máquina: JSON, HTML e log.
    ///
    /// Os três compartilham o mesmo nome base, <c>HOSTNAME_SERIAL_AAAAMMDD</c>, e a mesma
    /// pasta por cliente. É o que permite o analista jogar a visita inteira no consolidador
    /// e o que faz um relatório contestado ser rastreável até o log que o produziu.
    /// </summary>
    public static class ReportWriter
    {
        public static IList<string> WriteAll(
            JObject document,
            string baseDirectory,
            string clientName,
            RunLog log,
            DateTimeOffset when)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var directory = ReportNaming.ClientDirectory(baseDirectory, clientName);
            var baseName = ReportNaming.BaseName(document, when);

            var written = new List<string>
            {
                JsonReportWriter.Write(document, Path.Combine(directory, baseName + ".json")),
                HtmlReportWriter.Write(document, Path.Combine(directory, baseName + ".html"))
            };

            // O log é gravado por último e de propósito: se ele falhar, o diagnóstico já
            // está em disco. O inverso perderia o relatório por causa do arquivo acessório.
            if (log != null) written.Add(log.SaveTo(Path.Combine(directory, baseName + ".log")));

            return written;
        }
    }
}
