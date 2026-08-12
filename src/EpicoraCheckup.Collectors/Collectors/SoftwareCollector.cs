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
    /// Software instalado, lido do REGISTRO.
    ///
    /// **PROIBIDO usar <c>Win32_Product</c>** — a classe dispara reconfiguração de todo pacote
    /// MSI da máquina do cliente. Proibição do doc 02 §4.7 e regra 2 de contribuição, não
    /// preferência de performance.
    /// </summary>
    public sealed class SoftwareCollector : CollectorBase
    {
        private static readonly UninstallScope[] Scopes =
        {
            new UninstallScope(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM"),
            new UninstallScope(RegistryHive.LocalMachine,
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM-WOW6432"),
            new UninstallScope(RegistryHive.CurrentUser,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "HKCU")
        };

        public override string Id
        {
            get { return "software"; }
        }

        public override string DisplayName
        {
            get { return "Software instalado"; }
        }

        public override int EstimatedSeconds
        {
            get { return 8; }
        }

        protected override JObject Read(
            CollectionContext context, ErrorSink errors, CancellationToken cancellationToken)
        {
            var programs = new List<InstalledProgram>();

            foreach (var scope in Scopes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var subKeyName in RegistryReader.SubKeyNames(scope.Hive, scope.Path))
                {
                    // Uma abertura de chave por programa, não uma por valor: são centenas de
                    // programas numa máquina de escritório, e o orçamento da coleta inteira é
                    // de 90 s (doc 01 §4).
                    var entry = new PropertyBag(null,
                        RegistryReader.Values(scope.Hive, scope.Path + "\\" + subKeyName));

                    var name = entry.Text("DisplayName");
                    if (name == null) continue;

                    // Componente de sistema não é programa instalado do ponto de vista de quem
                    // lê o inventário: entra como ruído e infla a contagem.
                    if (entry.Int("SystemComponent") == 1) continue;

                    programs.Add(new InstalledProgram
                    {
                        DisplayName = name,
                        DisplayVersion = entry.Text("DisplayVersion"),
                        Publisher = entry.Text("Publisher"),
                        InstallDate = SoftwareFacts.InstallDate(entry.Raw("InstallDate")),
                        EstimatedSizeBytes = SoftwareFacts.SizeBytes(entry.Raw("EstimatedSize")),
                        Scope = scope.Label
                    });
                }
            }

            return SoftwareFacts.Build(programs);
        }

        protected override string Summarize(JObject data)
        {
            return data["count"] + " programas instalados";
        }

        private sealed class UninstallScope
        {
            public UninstallScope(RegistryHive hive, string path, string label)
            {
                Hive = hive;
                Path = path;
                Label = label;
            }

            public RegistryHive Hive { get; }

            public string Path { get; }

            public string Label { get; }
        }
    }

    /// <summary>Um programa como consta no registro de desinstalação.</summary>
    public sealed class InstalledProgram
    {
        public string DisplayName { get; set; }

        public string DisplayVersion { get; set; }

        public string Publisher { get; set; }

        public string InstallDate { get; set; }

        public long? EstimatedSizeBytes { get; set; }

        public string Scope { get; set; }
    }

    /// <summary>Derivação pura do payload de <c>software</c>.</summary>
    public static class SoftwareFacts
    {
        // As listas são as mesmas do protótipo, verbatim, inclusive os (?i) redundantes: quem
        // acrescentar um produto aqui precisa acrescentar lá, e um diff entre os dois arquivos
        // é o que torna isso verificável (ADR-009).
        private static readonly string[] RemoteAccess =
        {
            "(?i)teamviewer", "(?i)anydesk", @"(?i)\bvnc\b", "(?i)logmein", "(?i)splashtop",
            "(?i)gotoassist", "(?i)ammyy", "(?i)supremo", "(?i)rustdesk", "(?i)chrome remote desktop"
        };

        private static readonly string[] Edr =
        {
            "(?i)crowdstrike", "(?i)sentinelone", "(?i)sophos", "(?i)carbon black", "(?i)cylance",
            "(?i)cortex xdr", "(?i)defender for endpoint", "(?i)huntress", @"(?i)\bs1\b agent"
        };

        private static readonly string[] Antivirus =
        {
            "(?i)bitdefender", @"(?i)\beset\b", "(?i)kaspersky", "(?i)trend micro", "(?i)mcafee",
            "(?i)symantec", "(?i)norton", "(?i)avast", "(?i)avg ", "(?i)malwarebytes",
            "(?i)panda security", "(?i)f-secure", "(?i)webroot"
        };

        private static readonly string[] Backup =
        {
            "(?i)veeam", "(?i)acronis", "(?i)datto", "(?i)backup exec", "(?i)macrium",
            "(?i)cobian", "(?i)urbackup", "(?i)carbonite", "(?i)idrive", @"(?i)\bveritas\b"
        };

        private static readonly string[] Obsolete =
        {
            @"(?i)java\s*(se\s*)?(runtime\s*)?(environment\s*)?[1-8]\b", "(?i)adobe flash",
            "(?i)adobe shockwave", "(?i)microsoft silverlight", @"(?i)\.net framework [1-3]\."
        };

        private static readonly string[] PotentiallyUnwanted =
        {
            "(?i)advanced systemcare", "(?i)driver booster", "(?i)ccleaner", "(?i)pc ?optimizer",
            @"(?i)\bdriverpack\b", "(?i)wondershare", "(?i)mypc", "(?i)toolbar",
            "(?i)search protect", "(?i)web companion"
        };

        private static readonly Browser[] Browsers =
        {
            new Browser("Google Chrome", "(?i)^google chrome$"),
            new Browser("Mozilla Firefox", "(?i)^mozilla firefox"),
            new Browser("Microsoft Edge", "(?i)^microsoft edge$"),
            new Browser("Opera", @"(?i)^opera\b")
        };

        public static JObject Build(IList<InstalledProgram> programs)
        {
            // Ordenação ORDINAL, e não pela cultura da máquina: o mesmo parque coletado em
            // máquinas com idiomas diferentes tem que produzir a mesma lista, senão o diff
            // entre dois relatórios do mesmo cliente vira ruído.
            var unique = (programs ?? new List<InstalledProgram>())
                .GroupBy(program => program.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(program => program.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var data = new JObject();

            data["count"] = unique.Count;
            data["programs"] = Payload.Array(unique.Select(Program));

            data["classification"] = new JObject
            {
                ["remoteAccessTools"] = Payload.Texts(MatchAny(unique, RemoteAccess)),
                ["antivirusProducts"] = Payload.Texts(MatchAny(unique, Antivirus)),
                ["edrAgents"] = Payload.Texts(MatchAny(unique, Edr)),
                ["backupAgents"] = Payload.Texts(MatchAny(unique, Backup)),
                ["obsoleteRuntimes"] = Payload.Texts(MatchAny(unique, Obsolete)),
                ["potentiallyUnwanted"] = Payload.Texts(MatchAny(unique, PotentiallyUnwanted)),

                // Heurística de licenciamento NÃO implementada: é exposição jurídica e aguarda
                // revisão do jurídico. O texto do relatório, quando existir, é "podem exigir
                // revisão de licenciamento" — nunca afirmação de irregularidade (doc 03 §4.7).
                ["licenseReviewCandidates"] = new JArray()
            };

            data["browsers"] = Payload.ArrayOrNull(DetectBrowsers(unique));

            // latestKnownVersion exigiria tabela mantida, que não existe. Fica vazio e SW-003
            // resolve Indeterminate.
            data["outdatedBrowsers"] = new JArray();

            return Payload.Sanitized(data);
        }

        /// <summary>
        /// <c>InstallDate</c> do registro vem como <c>yyyyMMdd</c> sem separador. Qualquer outro
        /// formato vira <c>null</c> — data inventada num inventário é pior que data ausente.
        /// </summary>
        public static string InstallDate(object raw)
        {
            if (raw == null) return null;

            var text = Convert.ToString(raw, CultureInfo.InvariantCulture);
            if (text == null || !Regex.IsMatch(text, @"^\d{8}$")) return null;

            DateTime parsed;
            if (!DateTime.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out parsed))
            {
                return null;
            }

            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        /// <summary><c>EstimatedSize</c> está em KIB no registro. O schema guarda bytes.</summary>
        public static long? SizeBytes(object raw)
        {
            if (raw == null) return null;

            try
            {
                var kilobytes = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
                return kilobytes < 0 ? (long?)null : kilobytes * 1024L;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Programas cujo nome OU fabricante casa com algum padrão.
        ///
        /// É a classificação, e não a lista crua, que gera achado comercial: SEC-011 (acesso
        /// remoto), SEC-012 (backup) e SW-005 (antivírus de terceiro) leem daqui. As listas
        /// crescem com o campo.
        /// </summary>
        public static IList<string> MatchAny(IEnumerable<InstalledProgram> programs, string[] patterns)
        {
            var hits = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var program in programs)
            {
                var haystack = program.DisplayName + " " + program.Publisher;

                foreach (var pattern in patterns)
                {
                    if (!Regex.IsMatch(haystack, pattern, RegexOptions.IgnoreCase)) continue;

                    if (seen.Add(program.DisplayName)) hits.Add(program.DisplayName);
                    break;
                }
            }

            return hits;
        }

        private static IList<JObject> DetectBrowsers(IList<InstalledProgram> programs)
        {
            var found = new List<JObject>();

            foreach (var browser in Browsers)
            {
                var hit = programs.FirstOrDefault(
                    program => Regex.IsMatch(program.DisplayName, browser.Pattern, RegexOptions.IgnoreCase));

                if (hit == null) continue;

                found.Add(new JObject
                {
                    ["name"] = browser.Name,
                    ["version"] = hit.DisplayVersion,
                    ["latestKnownVersion"] = null,
                    ["outdated"] = null
                });
            }

            return found;
        }

        private static JObject Program(InstalledProgram program)
        {
            var entry = new JObject();

            entry["displayName"] = program.DisplayName;
            entry["displayVersion"] = program.DisplayVersion;
            entry["publisher"] = program.Publisher;
            entry["installDate"] = program.InstallDate;
            entry["estimatedSizeBytes"] = program.EstimatedSizeBytes;
            entry["scope"] = program.Scope;

            return entry;
        }

        private sealed class Browser
        {
            public Browser(string name, string pattern)
            {
                Name = name;
                Pattern = pattern;
            }

            public string Name { get; }

            public string Pattern { get; }
        }
    }
}
