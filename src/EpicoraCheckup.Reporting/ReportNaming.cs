using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Reporting
{
    /// <summary>
    /// Nome de arquivo e pasta de saída.
    ///
    /// **Espelha o protótipo PowerShell caractere por caractere.** O ADR-009 diz que o
    /// consolidador não distingue a origem; se as duas implementações nomeassem diferente,
    /// distinguiria — e a deduplicação por visita passaria a depender de qual ferramenta
    /// rodou.
    /// </summary>
    public static class ReportNaming
    {
        /// <summary>
        /// Caracteres inválidos em nome de arquivo no Windows, mais espaços em branco.
        /// Colapsados num único hífen.
        /// </summary>
        private static readonly Regex Invalid = new Regex(@"[\\/:*?""<>|\s]+", RegexOptions.Compiled);

        private const int MaxLength = 40;

        /// <summary>
        /// <c>HOSTNAME_SERIAL_AAAAMMDD</c>, sem extensão.
        ///
        /// Serial vem vazio ou com espaços em muitos fabricantes, então o fallback é
        /// determinístico — dois diagnósticos da mesma máquina no mesmo dia produzem o mesmo
        /// nome de propósito, e o mais recente substitui o anterior.
        /// </summary>
        public static string BaseName(JObject document, DateTimeOffset when)
        {
            var machine = MachineData(document);

            var hostname = Safe((string)machine?["hostname"], "HOST");
            var serial = Safe((string)machine?["productSerial"], "SEM-SERIAL");

            return $"{hostname}_{serial}_{when:yyyyMMdd}";
        }

        /// <summary>
        /// Pasta de saída do cliente, dentro da pasta base. Mesma estrutura do protótipo:
        /// uma pasta por cliente, para a visita inteira ficar junta.
        /// </summary>
        public static string ClientDirectory(string baseDirectory, string clientName)
        {
            return Path.Combine(baseDirectory, Safe(clientName, "CLIENTE"));
        }

        /// <summary>
        /// Sanitiza para uso em nome de arquivo. Devolve <paramref name="fallback"/> quando
        /// não sobra nada — nome de arquivo vazio derrubaria a gravação no fim da coleta,
        /// que é o pior momento possível para falhar.
        /// </summary>
        public static string Safe(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;

            var clean = Invalid.Replace(value, "-").Trim('-');
            if (string.IsNullOrWhiteSpace(clean)) return fallback;

            return clean.Length > MaxLength ? clean.Substring(0, MaxLength) : clean;
        }

        private static JObject MachineData(JObject document)
        {
            var collectors = document?["collectors"] as JArray;
            if (collectors == null) return null;

            foreach (var token in collectors)
            {
                var collector = token as JObject;
                if (collector != null && (string)collector["id"] == "machine")
                    return collector["data"] as JObject;
            }

            return null;
        }
    }
}
