using System;
using System.Collections.Generic;
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
    /// Sistema operacional e licenciamento. <c>productFamily</c> é o campo que sustenta o
    /// gancho comercial mais direto que existe hoje: Windows 10 sem suporte desde 14/10/2025.
    /// </summary>
    public sealed class OsCollector : CollectorBase
    {
        private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

        public override string Id
        {
            get { return "os"; }
        }

        public override string DisplayName
        {
            get { return "Sistema operacional e licenciamento"; }
        }

        public override int EstimatedSeconds
        {
            get { return 5; }
        }

        protected override JObject Read(
            CollectionContext context, ErrorSink errors, CancellationToken cancellationToken)
        {
            var os = Wmi.Instances(Wmi.CimV2, "Win32_OperatingSystem").FirstOrDefault();

            var activation = errors.Read("SoftwareLicensingProduct", ReadActivation);

            return OsFacts.Build(os, ReadCurrentVersion(), activation, DateTimeOffset.Now);
        }

        protected override string Summarize(JObject data)
        {
            var status = TextOf(data["activation"]["status"]);

            var ativacao = status == "Licensed" ? "ativado"
                : status == "Unknown" ? "ativação não verificada"
                : "NÃO ativado";

            return string.Format("{0} {1}, {2}",
                TextOf(data["caption"]), TextOf(data["displayVersion"]), ativacao);
        }

        /// <summary>
        /// Edição e UBR saem do REGISTRO, não do <c>caption</c>: caption é traduzido, e comparar
        /// texto localizado quebra em máquina com outro idioma.
        /// </summary>
        private static IDictionary<string, object> ReadCurrentVersion()
        {
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in new[] { "EditionID", "UBR", "DisplayVersion", "ReleaseId" })
            {
                var value = RegistryReader.Value(RegistryHive.LocalMachine, CurrentVersionKey, name);
                if (value != null) values[name] = value;
            }

            return values;
        }

        /// <summary>
        /// Estado de ativação.
        ///
        /// NOTA DE AUDITORIA: <c>PartialProductKey</c> aparece apenas na cláusula WHERE, como
        /// FILTRO — é o que distingue a licença do Windows instalado das dezenas de outras
        /// linhas que a classe devolve. A lista do SELECT tem só <c>LicenseStatus</c> e
        /// <c>ProductKeyChannel</c>. Nenhum fragmento de chave é lido, gravado ou registrado em
        /// log. A proibição do doc 01 §7.1 é sobre COLETAR chave, e não coletamos.
        ///
        /// O filtro vai na consulta e não em memória porque a classe é lenta.
        /// </summary>
        private static PropertyBag ReadActivation()
        {
            const string query =
                "SELECT LicenseStatus, ProductKeyChannel FROM SoftwareLicensingProduct " +
                "WHERE ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL";

            return Wmi.Query(Wmi.CimV2, query).FirstOrDefault();
        }
    }

    /// <summary>Derivação pura do payload de <c>os</c>.</summary>
    public static class OsFacts
    {
        private static readonly Dictionary<int, string> LicenseStates = new Dictionary<int, string>
        {
            { 0, "Unlicensed" }, { 1, "Licensed" }, { 2, "OutOfBox" }, { 3, "OutOfTolerance" },
            { 4, "NonGenuine" }, { 5, "Notification" }
        };

        public static JObject Build(
            PropertyBag os,
            IDictionary<string, object> currentVersion,
            PropertyBag activation,
            DateTimeOffset now)
        {
            var registry = new PropertyBag(null, currentVersion);

            var build = os == null ? 0 : os.Int("BuildNumber") ?? 0;
            var productType = os == null ? null : os.Int("ProductType");
            var isServer = productType.HasValue ? (bool?)(productType.Value != 1) : null;

            var editionId = registry.Text("EditionID");

            var installed = os == null ? null : os.Moment("InstallDate");
            var booted = os == null ? null : os.Moment("LastBootUpTime");

            var data = new JObject();

            data["caption"] = os == null ? null : os.Text("Caption");
            data["edition"] = editionId;
            data["productFamily"] = Family(build, isServer);
            data["isServer"] = isServer;
            data["isHomeEdition"] = IsHome(editionId);
            data["version"] = os == null ? null : os.Text("Version");
            data["buildNumber"] = build;
            data["ubr"] = registry.Int("UBR");
            data["displayVersion"] = registry.Text("DisplayVersion") ?? registry.Text("ReleaseId");
            data["architecture"] = os == null ? null : os.Text("OSArchitecture");
            data["installDate"] = Payload.Date(installed);
            data["installAgeYears"] = installed.HasValue
                ? (double?)Math.Round((now - installed.Value).TotalDays / 365.25, 1)
                : null;
            data["lastBootTime"] = Payload.Moment(booted);
            data["uptimeDays"] = booted.HasValue
                ? (double?)Math.Round((now - booted.Value).TotalDays, 2)
                : null;

            data["activation"] = Activation(activation);

            // ADR-005: a tabela de builds está vazia, então NÃO avaliamos. OS-005 resolve
            // Indeterminate em vez de acusar de desatualizada uma máquina em dia.
            data["buildFreshness"] = new JObject
            {
                ["evaluated"] = false,
                ["reason"] = "rules/windows-builds.json não preenchido — ver ADR-005",
                ["latestKnownBuild"] = null,
                ["latestKnownUbr"] = null,
                ["tableValidUntil"] = null,
                ["isCurrent"] = null
            };

            return Payload.Sanitized(data);
        }

        /// <summary>
        /// Família normalizada a partir da BUILD, nunca do <c>caption</c>: é o que OS-001 e
        /// OS-002 avaliam, e comparar caption por string quebra em máquina com idioma diferente.
        /// </summary>
        public static string Family(int build, bool? isServer)
        {
            if (isServer == true) return "Windows Server";

            if (build >= 22000) return "Windows 11";
            if (build >= 10240) return "Windows 10";
            if (build >= 9600) return "Windows 8.1";
            if (build >= 9200) return "Windows 8";
            if (build >= 7600) return "Windows 7";
            if (build > 0) return "Older";

            return "Unknown";
        }

        public static bool? IsHome(string editionId)
        {
            return editionId == null
                ? (bool?)null
                : Regex.IsMatch(editionId, "core|home", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// <c>SoftwareLicensingProduct</c> é lenta e costuma exigir elevação. Sem resposta o
        /// estado fica <c>Unknown</c> — que é honesto. **Nunca "não ativado"**: acusar de
        /// pirataria uma máquina licenciada é o pior falso positivo que este relatório pode
        /// produzir numa reunião com o cliente.
        /// </summary>
        private static JObject Activation(PropertyBag licence)
        {
            var code = licence == null ? null : licence.Int("LicenseStatus");

            return new JObject
            {
                ["status"] = Payload.Lookup(LicenseStates, code) ?? "Unknown",
                ["statusCode"] = code,
                ["channel"] = licence == null ? null : licence.Text("ProductKeyChannel")
            };
        }
    }
}
