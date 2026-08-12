using System;
using System.Collections.Generic;
using System.Linq;
using EpicoraCheckup.Core.Contracts;
using EpicoraCheckup.Core.Model;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Collectors
{
    /// <summary>
    /// Campos derivados que dependem de MAIS DE UM coletor.
    ///
    /// Feito aqui, uma vez, no fim da coleta, em vez de acoplar coletores entre si: um coletor
    /// que precisa do resultado de outro deixa de poder falhar sozinho, e o tempo limite de um
    /// passa a derrubar o outro.
    ///
    /// Roda sobre os RESULTADOS, antes da avaliação da matriz. Sem esta etapa,
    /// <c>antivirus.securitySoftwareInInventory</c> fica nulo e SEC-001 perde o cruzamento que
    /// impede o pior falso positivo do produto.
    /// </summary>
    public static class Consolidation
    {
        public static void Apply(IEnumerable<CollectorResult> results)
        {
            if (results == null) return;

            var byId = new Dictionary<string, JObject>(StringComparer.Ordinal);

            foreach (var result in results)
            {
                // Coletor que falhou ou foi ignorado não tem payload, e derivar de payload
                // ausente é como se inventa dado.
                if (result == null || result.Status != CollectorStatus.Completed) continue;

                var data = result.Data as JObject;
                if (data != null && result.Id != null) byId[result.Id] = data;
            }

            CrossCheckAntivirus(Get(byId, "antivirus"), Get(byId, "software"));

            EvaluateWindows11(
                Get(byId, "win11"), Get(byId, "memory"), Get(byId, "storage"), Get(byId, "cpu"));
        }

        private static JObject Get(IDictionary<string, JObject> byId, string id)
        {
            JObject data;
            return byId.TryGetValue(id, out data) ? data : null;
        }

        // ------------------------------------------------------------ antivírus × software

        /// <summary>
        /// CRUZAMENTO OBRIGATÓRIO (doc 03 §4.6).
        ///
        /// Impede o pior falso positivo possível: dizer "esta máquina está sem antivírus" para
        /// quem tem EDR corporativo que a Central de Segurança não enxerga. Com a lista
        /// preenchida, SEC-001 resolve Indeterminate em vez de NonCompliant.
        /// </summary>
        private static void CrossCheckAntivirus(JObject antivirus, JObject software)
        {
            if (antivirus == null) return;

            if (software != null)
            {
                var classification = software["classification"] as JObject;

                if (classification != null)
                {
                    var inventory = new List<string>();

                    foreach (var name in Names(classification["edrAgents"]).Concat(Names(classification["antivirusProducts"])))
                        if (!inventory.Contains(name, StringComparer.Ordinal)) inventory.Add(name);

                    // Lista vazia é resposta ("procuramos e não há"), diferente de null
                    // ("não foi possível cruzar"). SEC-001 lê a diferença.
                    antivirus["securitySoftwareInInventory"] = Payload.Texts(inventory);
                }
            }

            if ((bool?)antivirus["securityCenterAvailable"] == true)
            {
                var products = antivirus["products"] as JArray;
                antivirus["anyProtectionDetected"] = products != null && products.Count > 0;
            }
        }

        private static IEnumerable<string> Names(JToken token)
        {
            var array = token as JArray;
            if (array == null) yield break;

            foreach (var item in array)
            {
                var name = item.Type == JTokenType.String ? (string)item : null;
                if (!string.IsNullOrWhiteSpace(name)) yield return name;
            }
        }

        // ------------------------------------------------------------ Windows 11

        private const long Gibibyte = 1073741824L;

        /// <summary>
        /// Requisitos do Windows 11, e o veredito de elegibilidade.
        ///
        /// **<c>Unknown</c> NUNCA conta como <c>Fail</c>.** É a diferença entre "esta máquina
        /// não migra" — frase que vende troca de parque — e "não conseguimos avaliar este
        /// requisito". Por isso <c>eligible</c> tem três estados: <c>false</c> com bloqueio
        /// medido, <c>null</c> quando falta informação, <c>true</c> só com tudo confirmado.
        /// </summary>
        private static void EvaluateWindows11(JObject win11, JObject memory, JObject storage, JObject cpu)
        {
            if (win11 == null) return;

            var requirements = win11["requirements"] as JObject;
            if (requirements == null) return;

            requirements["tpm"] = TpmRequirement(win11["tpm"] as JObject);
            requirements["secureBoot"] = Requirement((bool?)Field(win11, "secureBoot", "enabled"));
            requirements["firmware"] = FirmwareRequirement((string)Field(win11, "firmware", "mode"));
            requirements["ram"] = Threshold((long?)memory?["totalGiB"], 4);
            requirements["storage"] = Threshold((long?)StorageSize(storage), 64L * Gibibyte);

            // CPU sem a lista oficial embutida é Unknown. NUNCA Fail (ADR-006): reprovar uma
            // CPU por não ter a lista é acusar de incompatível o que não foi medido.
            requirements["cpu"] = Requirement((bool?)cpu?["win11Supported"]);

            var blockers = new List<string>();
            var unknowns = new List<string>();

            foreach (var requirement in requirements)
            {
                var state = (string)requirement.Value;

                if (state == "Fail") blockers.Add(requirement.Key);
                else if (state == "Unknown") unknowns.Add(requirement.Key);
            }

            win11["blockers"] = Payload.Texts(blockers);
            win11["unknowns"] = Payload.Texts(unknowns);
            win11["eligible"] = blockers.Count > 0 ? false : unknowns.Count > 0 ? (bool?)null : true;
        }

        /// <summary>
        /// Campo de um sub-objeto, tolerante a bloco ausente. A consolidação roda sobre payload
        /// que pode ter vindo parcial de uma máquina que respondeu pela metade — e uma exceção
        /// aqui derrubaria a avaliação inteira depois de a coleta ter dado certo.
        /// </summary>
        private static JToken Field(JObject parent, string block, string field)
        {
            var inner = parent[block] as JObject;

            return inner == null ? null : inner[field];
        }

        private static long? StorageSize(JObject storage)
        {
            var disk = storage == null ? null : storage["systemDisk"] as JObject;

            return disk == null ? null : (long?)disk["sizeBytes"];
        }

        private static string TpmRequirement(JObject tpm)
        {
            if (tpm == null) return "Unknown";

            var present = (bool?)tpm["present"];
            if (present == false) return "Fail";

            var major = (double?)tpm["majorVersion"];
            if (present != true || !major.HasValue) return "Unknown";

            // TPM 2.0 desativado no firmware resolve com uma visita e cinco minutos na BIOS;
            // TPM ausente é máquina nova. Errar isso custa caro nos dois sentidos (W11-003).
            return major.Value >= 2 && (bool?)tpm["enabled"] != false ? "Pass" : "Fail";
        }

        private static string FirmwareRequirement(string mode)
        {
            if (mode == "UEFI") return "Pass";
            if (mode == "Legacy") return "Fail";

            return "Unknown";
        }

        private static string Requirement(bool? value)
        {
            if (value == true) return "Pass";
            if (value == false) return "Fail";

            return "Unknown";
        }

        private static string Threshold(long? value, long minimum)
        {
            if (!value.HasValue) return "Unknown";

            return value.Value >= minimum ? "Pass" : "Fail";
        }
    }
}
