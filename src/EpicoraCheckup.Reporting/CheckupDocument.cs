using System;
using System.Collections.Generic;
using EpicoraCheckup.Core.Contracts;
using EpicoraCheckup.Core.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace EpicoraCheckup.Reporting
{
    /// <summary>
    /// Monta o documento do schema 1.0 — a fonte única de verdade da saída.
    ///
    /// **Este é o mesmo documento em dois usos**, e isso é deliberado: montado sem
    /// <c>findings</c> ele é a ENTRADA do motor de regras; montado com eles é o arquivo que
    /// o consolidador lê. Duas montagens diferentes divergiriam, e a divergência apareceria
    /// como regra que dispara na ferramenta e não dispara no relatório.
    ///
    /// Concretamente: OS-004 lê <c>manual.corporateEnvironment</c>, que não está dentro de
    /// nenhum coletor. Avaliar sobre um documento que só tem <c>collectors</c> faz a regra
    /// perder a marcação do técnico em silêncio.
    ///
    /// Regras de serialização do doc 02 §5, todas impostas aqui:
    /// datas ISO 8601 **com offset**, tamanhos em bytes inteiros, e campo ausente é
    /// <c>null</c> — nunca zero, nunca string vazia, nunca "N/A".
    /// </summary>
    public static class CheckupDocument
    {
        public const string SchemaVersion = "1.0";

        /// <summary>
        /// "dotnet" ou "powershell". O consolidador não distingue a origem, mas a auditoria
        /// de um relatório contestado sim (ADR-009).
        /// </summary>
        public const string Runtime = "dotnet";

        private static readonly JsonSerializer FindingSerializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false, OverrideSpecifiedNames = true }
            },
            Converters = { new StringEnumConverter() },
            NullValueHandling = NullValueHandling.Include
        });

        public static JObject Build(ReportInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            return new JObject
            {
                ["schemaVersion"] = SchemaVersion,
                ["tool"] = BuildTool(input),
                ["execution"] = BuildExecution(input),
                ["client"] = BuildClient(input),
                ["manual"] = BuildManual(input),
                ["collectors"] = BuildCollectors(input.Collectors),
                ["findings"] = BuildFindings(input.Findings),
                ["score"] = BuildScore(input.Score)
            };
        }

        private static JObject BuildTool(ReportInput input)
        {
            return new JObject
            {
                ["name"] = "EpicoraCheckup",
                ["version"] = input.ToolVersion,
                ["commit"] = Text(input.Commit),
                ["runtime"] = Runtime,
                ["rulesVersion"] = Text(input.RulesVersion)
            };
        }

        private static JObject BuildExecution(ReportInput input)
        {
            var duration = input.FinishedAt - input.StartedAt;

            return new JObject
            {
                // "o" produz ISO 8601 com offset. Hora local sem offset é inútil no
                // consolidador, que compara máquinas de fusos e horários diferentes.
                ["startedAt"] = input.StartedAt.ToString("o"),
                ["finishedAt"] = input.FinishedAt.ToString("o"),
                ["durationSeconds"] = Math.Max(0, (int)duration.TotalSeconds),
                ["elevated"] = input.IsElevated,
                ["technician"] = Required(input.Identification?.Technician, "não informado"),
                ["diagnosticId"] = Required(input.Identification?.DiagnosticId, "SEM-NUMERO"),
                ["hostLocale"] = Text(input.HostLocale)
            };
        }

        private static JObject BuildClient(ReportInput input)
        {
            return new JObject
            {
                ["name"] = Required(input.Identification?.Client, "não informado"),
                ["unit"] = Text(input.Identification?.Unit)
            };
        }

        private static JObject BuildManual(ReportInput input)
        {
            var manual = input.Manual ?? new ManualData();

            return new JObject
            {
                ["machineLabel"] = Required(manual.MachineLabel, "não informado"),
                ["responsible"] = Required(manual.Responsible, "não informado"),
                ["department"] = Required(manual.Department, "não informado"),
                ["physicalLocation"] = Text(manual.PhysicalLocation),
                ["assetTag"] = Text(manual.AssetTag),
                ["physicalCondition"] = Text(manual.PhysicalCondition),
                ["notes"] = Text(manual.Notes),

                // true ou null, NUNCA false — igual ao protótipo. "Não marcado" e "marcado
                // como não corporativo" não são a mesma afirmação, e o schema aceita null.
                ["corporateEnvironment"] = input.Identification != null && input.Identification.CorporateEnvironment
                    ? (JToken)true
                    : JValue.CreateNull()
            };
        }

        private static JArray BuildCollectors(IEnumerable<CollectorResult> results)
        {
            var array = new JArray();
            if (results == null) return array;

            foreach (var result in results)
                array.Add(ToJson(result));

            return array;
        }

        private static JObject ToJson(CollectorResult result)
        {
            return new JObject
            {
                ["id"] = result.Id,
                ["displayName"] = result.DisplayName,
                ["status"] = result.Status.ToString(),
                ["skipReason"] = Text(result.SkipReason),
                ["requiresElevation"] = result.RequiresElevation,
                ["durationMs"] = result.DurationMs,
                ["timedOut"] = result.TimedOut,
                ["summary"] = Text(result.Summary),
                ["errors"] = ErrorsToJson(result.Errors),

                // Payload já é JToken quando vem de fixture, e objeto CLR quando vem de
                // coletor real. Os dois passam.
                ["data"] = result.Data == null
                    ? JValue.CreateNull()
                    : (result.Data as JToken ?? JToken.FromObject(result.Data))
            };
        }

        private static JArray ErrorsToJson(IEnumerable<CollectorError> errors)
        {
            var array = new JArray();
            if (errors == null) return array;

            foreach (var error in errors)
            {
                // `detail` fica de fora: o schema não o permite aqui, e ele carrega stack
                // trace. Vai para o log, que é do pacote interno, não do cliente.
                array.Add(new JObject
                {
                    ["source"] = Required(error.Source, "desconhecido"),
                    ["message"] = Required(error.Message, "sem detalhe")
                });
            }

            return array;
        }

        private static JArray BuildFindings(IEnumerable<Finding> findings)
        {
            var array = new JArray();
            if (findings == null) return array;

            foreach (var finding in findings)
                array.Add(JObject.FromObject(finding, FindingSerializer));

            return array;
        }

        private static JObject BuildScore(Score score)
        {
            // Antes da avaliação o score é neutro, igual ao protótipo, que também não avalia.
            // O schema exige o bloco, e um documento sem ele nem seria válido para conferir.
            if (score == null)
            {
                return new JObject
                {
                    ["value"] = 100,
                    ["band"] = ScoreBand.Green.ToString(),
                    ["verdict"] = Verdict.Keep.ToString(),
                    ["verdictDrivenBy"] = new JArray()
                };
            }

            return new JObject
            {
                ["value"] = score.Value,
                ["band"] = score.Band.ToString(),
                ["verdict"] = score.Verdict.ToString(),
                ["verdictDrivenBy"] = new JArray(score.VerdictDrivenBy ?? new List<string>())
            };
        }

        /// <summary>Vazio vira null, nunca string vazia — doc 02 §5.</summary>
        private static JToken Text(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? JValue.CreateNull() : new JValue(value);
        }

        /// <summary>
        /// Campo que o schema exige com pelo menos um caractere. Cai num marcador legível
        /// em vez de emitir documento inválido — relatório parcial vale mais que nenhum.
        /// </summary>
        private static JToken Required(string value, string fallback)
        {
            return new JValue(string.IsNullOrWhiteSpace(value) ? fallback : value);
        }
    }
}
