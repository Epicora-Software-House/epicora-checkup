using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using EpicoraCheckup.Collectors.Sources;
using EpicoraCheckup.Core.Contracts;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Collectors.Collectors
{
    /// <summary>
    /// Compatibilidade com Windows 11.
    ///
    /// **Não é marcado <c>RequiresElevation</c>, e isso foi MEDIDO EM CAMPO:** só o
    /// <c>Win32_Tpm</c> exige privilégio. Secure Boot (registro) e modo de firmware respondem
    /// sem elevação, e gatear o coletor inteiro perdia W11-004 e W11-005 de graça. O TPM
    /// degrada sozinho para null.
    ///
    /// <c>requirements</c>, <c>eligible</c>, <c>blockers</c> e <c>unknowns</c> saem vazios
    /// daqui: dependem de memória, disco e CPU, e são preenchidos em
    /// <see cref="Consolidation"/> — acoplar coletores entre si sairia mais caro.
    /// </summary>
    public sealed class Win11Collector : CollectorBase
    {
        private const string SecureBootKey = @"SYSTEM\CurrentControlSet\Control\SecureBoot\State";

        public override string Id
        {
            get { return "win11"; }
        }

        public override string DisplayName
        {
            get { return "Compatibilidade com Windows 11"; }
        }

        public override int EstimatedSeconds
        {
            get { return 3; }
        }

        protected override JObject Read(
            CollectionContext context, ErrorSink errors, CancellationToken cancellationToken)
        {
            var data = new JObject();

            data["tpm"] = ReadTpm(errors);
            data["secureBoot"] = ReadSecureBoot();
            data["firmware"] = ReadFirmware(errors);

            data["requirements"] = new JObject
            {
                ["cpu"] = "Unknown",
                ["tpm"] = "Unknown",
                ["secureBoot"] = "Unknown",
                ["firmware"] = "Unknown",
                ["ram"] = "Unknown",
                ["storage"] = "Unknown"
            };

            data["eligible"] = null;
            data["blockers"] = new JArray();
            data["unknowns"] = new JArray();

            return Payload.Sanitized(data);
        }

        protected override string Summarize(JObject data)
        {
            var present = FlagOf(data["tpm"]["present"]);

            if (present == false) return "TPM não detectado";
            if (present == null) return "TPM não pôde ser verificado";

            return string.Format("TPM {0}, firmware {1}",
                data["tpm"]["majorVersion"], TextOf(data["firmware"]["mode"]));
        }

        /// <summary>
        /// TPM.
        ///
        /// A distinção que importa: **namespace respondeu e não devolveu instância** é ausência
        /// confirmada de TPM (<c>false</c>); **namespace inacessível** é desconhecimento
        /// (<c>null</c>). Trocar um pelo outro faz a ferramenta dizer "esta máquina não tem TPM"
        /// para quem só rodou sem privilégio — e W11-003 decide entre uma visita de cinco
        /// minutos na BIOS e a troca da máquina.
        /// </summary>
        private static JObject ReadTpm(ErrorSink errors)
        {
            var tpm = new JObject
            {
                ["present"] = null,
                ["specVersionRaw"] = null,
                ["majorVersion"] = null,
                ["enabled"] = null,
                ["activated"] = null
            };

            IList<PropertyBag> instances;
            if (!errors.TryRead("Win32_Tpm", () => Wmi.Instances(Wmi.Tpm, "Win32_Tpm"), out instances))
                return tpm;

            var device = instances.FirstOrDefault();
            if (device == null)
            {
                tpm["present"] = false;
                return tpm;
            }

            var spec = device.Text("SpecVersion");

            tpm["present"] = true;
            tpm["specVersionRaw"] = spec;
            tpm["majorVersion"] = Win11Facts.SpecVersion(spec);
            tpm["enabled"] = device.Flag("IsEnabled_InitialValue");
            tpm["activated"] = device.Flag("IsActivated_InitialValue");

            return tpm;
        }

        private static JObject ReadSecureBoot()
        {
            var enabled = RegistryReader.Int(RegistryHive.LocalMachine, SecureBootKey, "UEFISecureBootEnabled");

            // Chave ausente normalmente significa firmware sem suporte a Secure Boot — é a
            // leitura do protótipo, mantida.
            return new JObject
            {
                ["enabled"] = enabled.HasValue ? (bool?)(enabled.Value == 1) : null,
                ["supported"] = enabled.HasValue,
                ["source"] = enabled.HasValue ? "registry" : "unavailable"
            };
        }

        private static JObject ReadFirmware(ErrorSink errors)
        {
            var mode = Win11Facts.FirmwareMode(Environment.GetEnvironmentVariable("firmware_type"));
            var method = mode == "Unknown" ? "unavailable" : "firmware_type";

            if (mode == "Unknown")
            {
                var system = errors
                    .Read("MSFT_Disk", () => Wmi.Instances(Wmi.Storage, "MSFT_Disk"))
                    ?.FirstOrDefault(disk => disk.Flag("IsSystem") == true);

                if (system != null)
                {
                    // Disco de sistema em GPT implica boot UEFI; MBR implica legado.
                    mode = system.Int("PartitionStyle") == 2 ? "UEFI" : "Legacy";
                    method = "partitionStyle";
                }
            }

            return new JObject { ["mode"] = mode, ["detectionMethod"] = method };
        }
    }

    /// <summary>Derivação pura do payload de <c>win11</c>.</summary>
    public static class Win11Facts
    {
        private static readonly Regex LeadingVersion = new Regex(@"^\d+(\.\d+)?", RegexOptions.CultureInvariant);

        /// <summary>
        /// Versão principal a partir de <c>SpecVersion</c>, que vem como <c>"2.0, 0, 1.38"</c> —
        /// versão da especificação, revisão e versão do firmware, e só a primeira interessa.
        /// </summary>
        public static double? SpecVersion(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var first = raw.Split(',')[0].Trim();

            var match = LeadingVersion.Match(first);
            if (!match.Success) return null;

            double value;
            return double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? (double?)value
                : null;
        }

        public static string FirmwareMode(string firmwareType)
        {
            if (string.IsNullOrWhiteSpace(firmwareType)) return "Unknown";

            if (Regex.IsMatch(firmwareType, "uefi", RegexOptions.IgnoreCase)) return "UEFI";
            if (Regex.IsMatch(firmwareType, "legacy", RegexOptions.IgnoreCase)) return "Legacy";

            return "Unknown";
        }
    }
}
