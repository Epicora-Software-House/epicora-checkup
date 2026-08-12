using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using EpicoraCheckup.Collectors.Sources;
using EpicoraCheckup.Core.Contracts;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Collectors.Collectors
{
    /// <summary>
    /// Processador. Separado de <see cref="MemoryCollector"/> — o documento funcional §5 lista
    /// "Processador e memória" como uma etapa, mas são domínios com falha independente e o
    /// tempo limite de um não deve derrubar o outro.
    /// </summary>
    public sealed class CpuCollector : CollectorBase
    {
        public override string Id
        {
            get { return "cpu"; }
        }

        public override string DisplayName
        {
            get { return "Processador"; }
        }

        public override int EstimatedSeconds
        {
            get { return 2; }
        }

        protected override JObject Read(
            CollectionContext context, ErrorSink errors, CancellationToken cancellationToken)
        {
            // Máquina com dois sockets devolve duas instâncias. O primeiro é o que as regras
            // avaliam — o parque da Epicora é estação de trabalho, não servidor de dois sockets.
            var processor = Wmi.Instances(Wmi.CimV2, "Win32_Processor").FirstOrDefault();

            return CpuFacts.Build(processor);
        }

        protected override string Summarize(JObject data)
        {
            return TextOf(data["name"]) + ", " + data["physicalCores"] + " núcleos";
        }
    }

    /// <summary>Derivação pura do payload de <c>cpu</c>.</summary>
    public static class CpuFacts
    {
        public static JObject Build(PropertyBag processor)
        {
            var raw = processor == null ? null : processor.Text("Name");

            var data = new JObject();

            data["name"] = raw;
            data["normalizedName"] = Normalize(raw);
            data["manufacturer"] = processor == null ? null : processor.Text("Manufacturer");
            data["physicalCores"] = processor == null ? null : processor.Int("NumberOfCores");
            data["logicalProcessors"] = processor == null ? null : processor.Int("NumberOfLogicalProcessors");
            data["maxClockMhz"] = processor == null ? null : processor.Int("MaxClockSpeed");
            data["socket"] = processor == null ? null : processor.Text("SocketDesignation");
            data["architecture"] = Architecture(processor == null ? null : processor.Int("AddressWidth"));
            data["virtualizationFirmwareEnabled"] =
                processor == null ? null : processor.Flag("VirtualizationFirmwareEnabled");

            // A lista oficial de CPUs do Windows 11 ainda não está embutida (ADR-006). O par
            // null + basis diz "não foi avaliado" — NUNCA "não suportado". A diferença decide
            // se a proposta é migração ou troca de máquina.
            data["win11Supported"] = null;
            data["win11SupportBasis"] = "listMissing";

            return Payload.Sanitized(data);
        }

        /// <summary>
        /// Normaliza o nome comercial para casar com a lista oficial de CPUs (ADR-006).
        ///
        /// Os padrões são os mesmos do protótipo, na mesma ordem, e todos sem diferenciar
        /// maiúsculas — <c>-replace</c> do PowerShell é insensível por padrão, e traduzir isso
        /// para <see cref="Regex"/> sem <see cref="RegexOptions.IgnoreCase"/> mudaria o
        /// resultado em silêncio.
        /// </summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var name = raw;

            name = Replace(name, @"\((R|TM|C)\)", string.Empty);
            name = Replace(name, @"\s*CPU\s*@.*$", string.Empty);
            name = Replace(name, @"\s*@\s*[\d.]+\s*GHz.*$", string.Empty);

            // "CPU" solto, longe do @: "Intel(R) Celeron(R) CPU N4020 @ 1.10GHz" sobrava como
            // "Intel Celeron CPU N4020", e a lista oficial escreve "Intel Celeron N4020" — o
            // nome normalizado não casaria com ela. Passo que o protótipo não tem; entrou aqui
            // e no .ps1 juntos (ADR-009).
            name = Replace(name, @"\bCPU\b", " ");
            name = Replace(name, @"^\d+(st|nd|rd|th)\s+Gen\s+", string.Empty);
            name = Replace(name, @"\s+with\s+Radeon.*$", string.Empty);
            name = Replace(name, @"\s+", " ");

            name = name.Trim();

            return name.Length == 0 ? null : name;
        }

        private static string Replace(string input, string pattern, string replacement)
        {
            return Regex.Replace(input, pattern, replacement,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string Architecture(int? addressWidth)
        {
            if (addressWidth == 64) return "x64";
            if (addressWidth == 32) return "x86";

            return null;
        }
    }
}
