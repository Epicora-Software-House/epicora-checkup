using System.Collections.Generic;
using System.Linq;
using EpicoraCheckup.Collectors;
using EpicoraCheckup.Core.Contracts;
using EpicoraCheckup.Core.Model;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EpicoraCheckup.Collectors.Tests
{
    /// <summary>
    /// Consolidação — os campos que dependem de mais de um coletor.
    ///
    /// Os dois que importam comercialmente: o cruzamento antivírus × software, que impede
    /// dizer "sem antivírus" para quem tem EDR corporativo, e a elegibilidade de Windows 11,
    /// que vira a frase "X das Y máquinas não migram" no relatório executivo.
    /// </summary>
    public sealed class ConsolidacaoTests
    {
        [Fact]
        public void EDR_no_inventario_de_software_chega_ao_bloco_de_antivirus()
        {
            // CRUZAMENTO OBRIGATÓRIO (doc 03 §4.6): sem ele, máquina com CrowdStrike e sem
            // registro na Central de Segurança sairia como "sem antivírus" — o pior falso
            // positivo que este produto pode cometer.
            var resultados = new[]
            {
                Resultado("antivirus", new JObject
                {
                    ["securityCenterAvailable"] = true,
                    ["products"] = new JArray(),
                    ["securitySoftwareInInventory"] = null,
                    ["anyProtectionDetected"] = null
                }),
                Resultado("software", new JObject
                {
                    ["classification"] = new JObject
                    {
                        ["edrAgents"] = new JArray("Falcon Sensor"),
                        ["antivirusProducts"] = new JArray("Bitdefender Endpoint Security Tools")
                    }
                })
            };

            Consolidation.Apply(resultados);

            var inventario = resultados[0].Data as JObject;

            Assert.Equal(2, inventario["securitySoftwareInInventory"].Count());
            Assert.Equal("Falcon Sensor", (string)inventario["securitySoftwareInInventory"][0]);
            Assert.False((bool)inventario["anyProtectionDetected"]);
        }

        [Fact]
        public void Coletor_de_software_que_falhou_deixa_o_cruzamento_nulo_em_vez_de_vazio()
        {
            // Lista vazia é resposta ("procuramos e não há"); null é ausência de resposta
            // ("não foi possível cruzar"). SEC-001 lê a diferença.
            var resultados = new[]
            {
                Resultado("antivirus", new JObject
                {
                    ["securityCenterAvailable"] = true,
                    ["products"] = new JArray(),
                    ["securitySoftwareInInventory"] = null
                }),
                Falhou("software")
            };

            Consolidation.Apply(resultados);

            var inventario = resultados[0].Data as JObject;

            Assert.Equal(JTokenType.Null, inventario["securitySoftwareInInventory"].Type);
        }

        [Fact]
        public void Maquina_sem_TPM_e_bloqueada_para_Windows_11_com_o_motivo_nomeado()
        {
            var resultados = new[]
            {
                Resultado("win11", Win11(tpmPresente: false, secureBoot: false, firmware: "Legacy")),
                Resultado("memory", new JObject { ["totalGiB"] = 8 }),
                Resultado("storage", new JObject
                {
                    ["systemDisk"] = new JObject { ["sizeBytes"] = 256060514304L }
                }),
                Resultado("cpu", new JObject { ["win11Supported"] = null })
            };

            Consolidation.Apply(resultados);

            var win11 = resultados[0].Data as JObject;

            Assert.False((bool)win11["eligible"]);
            Assert.Contains("tpm", Textos(win11["blockers"]));
            Assert.Contains("secureBoot", Textos(win11["blockers"]));
            Assert.Contains("firmware", Textos(win11["blockers"]));

            // RAM e disco passam, e a CPU segue desconhecida por falta da lista oficial.
            Assert.Equal(new[] { "cpu" }, Textos(win11["unknowns"]));
        }

        [Fact]
        public void Requisito_desconhecido_nunca_conta_como_reprovado()
        {
            // A diferença entre "esta máquina não migra" — frase que vende troca de parque — e
            // "não conseguimos avaliar". eligible fica null, que não é nem um nem outro.
            var resultados = new[]
            {
                Resultado("win11", Win11(tpmPresente: null, secureBoot: true, firmware: "UEFI")),
                Resultado("memory", new JObject { ["totalGiB"] = 16 }),
                Resultado("storage", new JObject
                {
                    ["systemDisk"] = new JObject { ["sizeBytes"] = 512110190592L }
                }),
                Resultado("cpu", new JObject { ["win11Supported"] = null })
            };

            Consolidation.Apply(resultados);

            var win11 = resultados[0].Data as JObject;

            Assert.Equal(JTokenType.Null, win11["eligible"].Type);
            Assert.Empty(win11["blockers"]);
            Assert.Equal(new[] { "cpu", "tpm" }, Textos(win11["unknowns"]));
        }

        [Fact]
        public void TPM_2_0_desativado_no_firmware_reprova_o_requisito_mas_com_TPM_presente()
        {
            // W11-003: TPM 2.0 desativado resolve com cinco minutos na BIOS; TPM ausente é
            // máquina nova. Errar isso custa caro nos dois sentidos.
            var win11 = Win11(tpmPresente: true, secureBoot: true, firmware: "UEFI");
            win11["tpm"]["majorVersion"] = 2.0;
            win11["tpm"]["enabled"] = false;

            var resultados = new[]
            {
                Resultado("win11", win11),
                Resultado("memory", new JObject { ["totalGiB"] = 16 }),
                Resultado("storage", new JObject
                {
                    ["systemDisk"] = new JObject { ["sizeBytes"] = 512110190592L }
                }),
                Resultado("cpu", new JObject { ["win11Supported"] = true })
            };

            Consolidation.Apply(resultados);

            Assert.Equal("Fail", (string)win11["requirements"]["tpm"]);
            Assert.False((bool)win11["eligible"]);
        }

        [Fact]
        public void Maquina_completa_e_elegivel_sem_ressalva()
        {
            var win11 = Win11(tpmPresente: true, secureBoot: true, firmware: "UEFI");
            win11["tpm"]["majorVersion"] = 2.0;
            win11["tpm"]["enabled"] = true;

            var resultados = new[]
            {
                Resultado("win11", win11),
                Resultado("memory", new JObject { ["totalGiB"] = 16 }),
                Resultado("storage", new JObject
                {
                    ["systemDisk"] = new JObject { ["sizeBytes"] = 512110190592L }
                }),
                Resultado("cpu", new JObject { ["win11Supported"] = true })
            };

            Consolidation.Apply(resultados);

            Assert.True((bool)win11["eligible"]);
            Assert.Empty(win11["blockers"]);
            Assert.Empty(win11["unknowns"]);
        }

        [Fact]
        public void Bloco_ausente_no_payload_nao_derruba_a_consolidacao()
        {
            // A coleta pode ter vindo parcial de uma máquina que respondeu pela metade. Uma
            // exceção aqui perderia a avaliação inteira depois de a coleta ter dado certo.
            var resultados = new[] { Resultado("win11", new JObject { ["requirements"] = new JObject() }) };

            Consolidation.Apply(resultados);

            Assert.Equal("Unknown", (string)((JObject)resultados[0].Data)["requirements"]["tpm"]);
        }

        // ------------------------------------------------------------ auxiliares

        private static JObject Win11(bool? tpmPresente, bool secureBoot, string firmware)
        {
            return new JObject
            {
                ["tpm"] = new JObject
                {
                    ["present"] = tpmPresente,
                    ["majorVersion"] = null,
                    ["enabled"] = null
                },
                ["secureBoot"] = new JObject { ["enabled"] = secureBoot },
                ["firmware"] = new JObject { ["mode"] = firmware },
                ["requirements"] = new JObject
                {
                    ["cpu"] = "Unknown",
                    ["tpm"] = "Unknown",
                    ["secureBoot"] = "Unknown",
                    ["firmware"] = "Unknown",
                    ["ram"] = "Unknown",
                    ["storage"] = "Unknown"
                },
                ["eligible"] = null,
                ["blockers"] = new JArray(),
                ["unknowns"] = new JArray()
            };
        }

        private static CollectorResult Resultado(string id, JObject dados)
        {
            return new CollectorResult
            {
                Id = id,
                Status = CollectorStatus.Completed,
                Data = dados,
                Errors = new List<CollectorError>()
            };
        }

        private static CollectorResult Falhou(string id)
        {
            return new CollectorResult
            {
                Id = id,
                Status = CollectorStatus.Failed,
                Data = null,
                Errors = new List<CollectorError>()
            };
        }

        private static string[] Textos(JToken token)
        {
            var array = (JArray)token;
            var textos = new string[array.Count];

            for (var i = 0; i < array.Count; i++) textos[i] = (string)array[i];

            return textos;
        }
    }
}
