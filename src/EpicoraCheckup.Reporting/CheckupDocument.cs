using System;
using System.Globalization;
using System.Text.RegularExpressions;
using EpicoraCheckup.Rules;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Reporting
{
    /// <summary>
    /// Monta o documento completo do schema 1.0 — a fonte única de verdade de uma execução,
    /// e o insumo do consolidador.
    ///
    /// O bloco <c>collectors</c> vem de <see cref="CollectionDocumentBuilder"/> e os blocos
    /// <c>findings</c>/<c>score</c> saem de <see cref="CheckupJson"/>, os mesmos que o motor
    /// de regras usa. Repetir a serialização aqui faria o relatório e os golden files
    /// divergirem sem ninguém perceber.
    /// </summary>
    public static class CheckupDocument
    {
        public const string SchemaVersion = "1.0";

        public static JObject Build(CheckupRun run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));

            var document = new JObject();

            document["schemaVersion"] = SchemaVersion;

            document["tool"] = new JObject
            {
                ["name"] = "EpicoraCheckup",
                ["version"] = Version(run.ToolVersion),
                ["commit"] = run.Commit,

                // Auditoria de relatório contestado: qual implementação produziu este número.
                // O protótipo grava "powershell" no mesmo campo (ADR-009).
                ["runtime"] = "dotnet",
                ["rulesVersion"] = run.RulesVersion
            };

            document["execution"] = new JObject
            {
                ["startedAt"] = Moment(run.StartedAt),
                ["finishedAt"] = Moment(run.FinishedAt),
                ["durationSeconds"] = run.DurationSeconds,

                // Falso aqui explica, para quem ler o relatório meses depois, por que TPM,
                // BitLocker e SMART aparecem como "não foi possível verificar".
                ["elevated"] = run.Elevated,
                ["technician"] = Required(run.Technician, "não informado"),
                ["diagnosticId"] = Required(run.DiagnosticId, "sem-identificador"),
                ["hostLocale"] = run.HostLocale
            };

            document["client"] = new JObject
            {
                ["name"] = Required(run.ClientName, "não informado"),
                ["unit"] = Blank(run.ClientUnit)
            };

            document["manual"] = new JObject
            {
                ["machineLabel"] = Required(run.MachineLabel, Hostname(run) ?? "sem-etiqueta"),
                ["responsible"] = Required(run.Responsible, "não informado"),
                ["department"] = Required(run.Department, "não informado"),
                ["physicalLocation"] = Blank(run.PhysicalLocation),
                ["assetTag"] = Blank(run.AssetTag),
                ["physicalCondition"] = Blank(run.PhysicalCondition),
                ["notes"] = Blank(run.Notes),
                ["corporateEnvironment"] = run.CorporateEnvironment
            };

            document["collectors"] = CollectionDocumentBuilder.FromResults(run.Collectors)["collectors"];

            var evaluation = JObject.Parse(CheckupJson.Serialize(new Core.Model.EvaluationResult
            {
                Findings = run.Findings,
                Score = run.Score
            }));

            document["findings"] = evaluation["findings"] ?? new JArray();
            document["score"] = evaluation["score"] ?? DefaultScore();

            // Fase 5. Ausente enquanto a fase não existir — e null é diferente de
            // "executou e não fez nada".
            document["optimization"] = JValue.CreateNull();

            return document;
        }

        /// <summary>Hostname coletado, para servir de rótulo quando o técnico não preencheu um.</summary>
        public static string Hostname(CheckupRun run)
        {
            foreach (var result in run.Collectors)
            {
                if (result == null || result.Id != "machine") continue;

                var data = result.Data as JObject;
                if (data == null) continue;

                var hostname = data["hostname"];
                if (hostname != null && hostname.Type == JTokenType.String) return (string)hostname;
            }

            return null;
        }

        public static string ProductSerial(CheckupRun run)
        {
            foreach (var result in run.Collectors)
            {
                if (result == null || result.Id != "machine") continue;

                var data = result.Data as JObject;
                if (data == null) continue;

                var serial = data["productSerial"];
                if (serial != null && serial.Type == JTokenType.String) return (string)serial;
            }

            return null;
        }

        // ------------------------------------------------------------ normalização

        private static readonly Regex TresNumeros = new Regex(@"^\d+\.\d+\.\d+", RegexOptions.CultureInvariant);

        /// <summary>
        /// Versão em <c>maior.menor.correção</c>, que é o que o schema aceita.
        ///
        /// O assembly informa quatro números, e o CI pode acrescentar <c>+sha</c>. Os dois
        /// formatos falhariam a validação, e um JSON fora do schema não entra no consolidador.
        /// </summary>
        public static string Version(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "0.0.0";

            var match = TresNumeros.Match(raw.Trim());

            return match.Success ? match.Value : "0.0.0";
        }

        private static string Required(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        /// <summary>Texto opcional: vazio vira <c>null</c>, nunca string vazia (doc 02 §5).</summary>
        private static JToken Blank(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? (JToken)JValue.CreateNull() : new JValue(value.Trim());
        }

        private static JValue Moment(DateTimeOffset moment)
        {
            return new JValue(moment.ToString("o", CultureInfo.InvariantCulture));
        }

        private static JObject DefaultScore()
        {
            // Sem avaliação não se inventa nota: 100/Verde/Manter só apareceria aqui se o motor
            // não tivesse rodado, e o bloco existe porque o schema o exige.
            return new JObject
            {
                ["value"] = 100,
                ["band"] = "Green",
                ["verdict"] = "Keep",
                ["verdictDrivenBy"] = new JArray()
            };
        }
    }
}
