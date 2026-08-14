using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EpicoraCheckup.Core.Contracts;
using EpicoraCheckup.Core.Model;
using EpicoraCheckup.Reporting;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EpicoraCheckup.Rules.Tests
{
    /// <summary>
    /// Montagem do documento de saída e as regras de serialização do doc 02 §5.
    /// </summary>
    public sealed class ReportingTests
    {
        // ---------------------------------------------------------------- o bug que isto pega

        [Theory]
        [InlineData(true, RuleState.NonCompliant)]
        [InlineData(false, RuleState.Compliant)]
        public void Marcacao_de_ambiente_corporativo_chega_ate_OS004(bool corporativo, RuleState esperado)
        {
            // OS-004 dispara com edição Home E (máquina em domínio OU marcação do técnico).
            // Aqui a máquina NÃO está em domínio, então a marcação é o único sinal — é
            // exatamente o escritório pequeno onde a edição Home aparece.
            //
            // Nenhuma das três fixtures isola este caminho, e foi assim que a primeira versão
            // do builder passou despercebida montando um documento só com `collectors`:
            // manual.corporateEnvironment resolvia ausente e a regra perdia a marcação em
            // silêncio, sem erro e sem teste vermelho.
            var input = new ReportInput
            {
                Identification = new Identification
                {
                    Technician = "T", Client = "C", DiagnosticId = "D",
                    CorporateEnvironment = corporativo
                },
                Manual = new ManualData { MachineLabel = "M", Responsible = "R", Department = "S" },
                StartedAt = DateTimeOffset.Now,
                FinishedAt = DateTimeOffset.Now,
                Collectors = new List<CollectorResult>
                {
                    Completed("os", "Sistema operacional", new JObject { ["isHomeEdition"] = true }),
                    Completed("machine", "Identificação", new JObject { ["domainJoined"] = false })
                }
            };

            var finding = Evaluate(CheckupDocument.Build(input)).Single(f => f.RuleId == "OS-004");

            Assert.Equal(esperado, finding.State);
        }

        [Fact]
        public void Documento_montado_reproduz_a_avaliacao_da_fixture()
        {
            // Prova geral do caminho: fixture → resultados de coletor → documento montado →
            // motor. Tem que dar o mesmo que avaliar a fixture direto.
            foreach (var nome in new[] { "sintetica-verde", "sintetica-amarela", "sintetica-vermelha" })
            {
                var fixture = LoadFixture(nome);
                var input = InputFrom(fixture);

                var pelaFixture = Evaluate(fixture);
                var peloDocumento = Evaluate(CheckupDocument.Build(input));

                Assert.Equal(
                    pelaFixture.Select(f => f.RuleId + "=" + f.State),
                    peloDocumento.Select(f => f.RuleId + "=" + f.State));
            }
        }

        // ---------------------------------------------------------------- serialização

        [Fact]
        public void Datas_saem_em_ISO_8601_com_offset()
        {
            var doc = CheckupDocument.Build(MinimalInput());

            // Hora local sem offset é inútil no consolidador, que compara máquinas de fusos
            // diferentes. O schema rejeita, mas o teste falha antes com mensagem melhor.
            foreach (var campo in new[] { "startedAt", "finishedAt" })
            {
                var valor = (string)doc["execution"][campo];
                Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?([+-]\d{2}:\d{2}|Z)$", valor);
            }
        }

        [Fact]
        public void Campo_ausente_vira_null_e_nunca_string_vazia()
        {
            var input = MinimalInput();
            input.Identification.Unit = "   ";
            input.Manual.Notes = string.Empty;

            var doc = CheckupDocument.Build(input);

            // Zero, string vazia e "N/A" destroem a análise no consolidador (doc 02 §5).
            Assert.Equal(JTokenType.Null, doc["client"]["unit"].Type);
            Assert.Equal(JTokenType.Null, doc["manual"]["notes"].Type);
        }

        [Fact]
        public void CorporateEnvironment_e_true_ou_null_nunca_false()
        {
            // Igual ao protótipo. "Não marcado" e "marcado como não corporativo" não são a
            // mesma afirmação, e emitir false diria a segunda quando só se sabe a primeira.
            var input = MinimalInput();

            input.Identification.CorporateEnvironment = false;
            Assert.Equal(JTokenType.Null, CheckupDocument.Build(input)["manual"]["corporateEnvironment"].Type);

            input.Identification.CorporateEnvironment = true;
            Assert.True((bool)CheckupDocument.Build(input)["manual"]["corporateEnvironment"]);
        }

        [Fact]
        public void Runtime_declara_dotnet()
        {
            // O consolidador não distingue a origem, mas a auditoria de um relatório
            // contestado sim (ADR-009).
            Assert.Equal("dotnet", (string)CheckupDocument.Build(MinimalInput())["tool"]["runtime"]);
            Assert.Equal("1.0", (string)CheckupDocument.Build(MinimalInput())["schemaVersion"]);
        }

        [Fact]
        public void Detalhe_de_erro_nao_vai_para_o_JSON_do_cliente()
        {
            var input = MinimalInput();
            input.Collectors = new List<CollectorResult>
            {
                new CollectorResult
                {
                    Id = "storage", DisplayName = "Armazenamento", Status = CollectorStatus.Failed,
                    Errors = new List<CollectorError>
                    {
                        new CollectorError { Source = "MSFT_PhysicalDisk", Message = "acesso negado", Detail = "stack trace inteiro" }
                    }
                }
            };

            var erro = (JObject)CheckupDocument.Build(input)["collectors"][0]["errors"][0];

            // `detail` carrega stack trace e vai só para o log, que é do pacote interno.
            // O schema também proíbe, com additionalProperties false.
            Assert.False(erro.ContainsKey("detail"));
            Assert.Equal("acesso negado", (string)erro["message"]);
        }

        // ---------------------------------------------------------------- nome de arquivo

        [Theory]
        [InlineData("DELL-G15", "41THSY3", "DELL-G15_41THSY3_20260804")]
        [InlineData("DELL G15", "  ", "DELL-G15_SEM-SERIAL_20260804")]
        [InlineData(null, null, "HOST_SEM-SERIAL_20260804")]
        [InlineData("host/inv:al*id", "a\\b", "host-inv-al-id_a-b_20260804")]
        public void Nome_base_espelha_o_prototipo(string hostname, string serial, string esperado)
        {
            var doc = new JObject
            {
                ["collectors"] = new JArray
                {
                    new JObject
                    {
                        ["id"] = "machine",
                        ["data"] = new JObject
                        {
                            ["hostname"] = hostname == null ? JValue.CreateNull() : new JValue(hostname),
                            ["productSerial"] = serial == null ? JValue.CreateNull() : new JValue(serial)
                        }
                    }
                }
            };

            var quando = new DateTimeOffset(2026, 8, 4, 10, 0, 0, TimeSpan.FromHours(-3));

            Assert.Equal(esperado, ReportNaming.BaseName(doc, quando));
        }

        [Fact]
        public void Nome_longo_e_cortado_em_40_caracteres()
        {
            Assert.Equal(40, ReportNaming.Safe(new string('x', 80), "FALLBACK").Length);
        }

        // ---------------------------------------------------------------- HTML

        [Fact]
        public void Html_e_autocontido_e_escapa_texto_do_tecnico()
        {
            var input = MinimalInput();
            input.Manual.Notes = "usuário reclamou de <script>alert(1)</script> & lentidão";

            var html = HtmlReportWriter.Render(CheckupDocument.Build(input));

            // Precisa abrir em máquina sem internet e continuar legível daqui a cinco anos.
            //
            // A verificação é por REFERÊNCIA QUE O NAVEGADOR BUSCA, e não por ocorrência da
            // string "http". Procurar a string voltava falso positivo desde que a licença da
            // fonte passou a viajar no rodapé: o texto da SIL OFL cita a URL da própria
            // licença, que é citação e não recurso a carregar. O que quebra a promessa é
            // src=, href= e url() apontando para fora — e é isso que está testado aqui.
            foreach (var atributo in new[] { "src=\"http", "src='http", "href=\"http", "href='http", "url(http", "url('http", "url(\"http" })
                Assert.DoesNotContain(atributo, html);

            Assert.DoesNotContain("<script", html);
            Assert.Contains("&lt;script&gt;", html);
            Assert.Contains("&amp;", html);

            // Impressão em A4 tem que funcionar: o técnico às vezes entrega em papel.
            Assert.Contains("@media print", html);
            Assert.Contains("size:A4", html);
        }

        // ---------------------------------------------------------------- amostra para o schema

        [Fact]
        public void Grava_amostra_para_validacao_de_schema()
        {
            // Não valida contra o schema aqui: não há validador de JSON Schema decente e
            // gratuito para net472. Grava a amostra, e o CI roda tools/validate-schema.mjs
            // em cima dela — o mesmo validador que já cobre as fixtures.
            var destino = Path.Combine(RepositoryLayout.Root, "tests", "generated");
            Directory.CreateDirectory(destino);

            foreach (var nome in new[] { "sintetica-verde", "sintetica-amarela", "sintetica-vermelha" })
            {
                var fixture = LoadFixture(nome);
                var input = InputFrom(fixture);

                var avaliacao = new RuleEngine(Rules()).Evaluate(CheckupDocument.Build(input));
                input.Findings = avaliacao.Result.Findings;
                input.Score = avaliacao.Result.Score;

                var caminho = Path.Combine(destino, "documento-" + nome + ".json");
                JsonReportWriter.Write(CheckupDocument.Build(input), caminho);

                Assert.True(File.Exists(caminho));
            }
        }

        // ---------------------------------------------------------------- helpers

        private static IReadOnlyList<Rule> Rules() => RuleRepository.LoadFromDirectory(RepositoryLayout.RulesDirectory);

        private static IList<Finding> Evaluate(JObject document) =>
            new RuleEngine(Rules()).Evaluate(document, includePending: true).Result.Findings;

        private static CollectorResult Completed(string id, string displayName, JObject data)
        {
            return new CollectorResult
            {
                Id = id,
                DisplayName = displayName,
                Status = CollectorStatus.Completed,
                Errors = new List<CollectorError>(),
                Data = data
            };
        }

        private static ReportInput MinimalInput()
        {
            return new ReportInput
            {
                Identification = new Identification { Technician = "Gabriel", Client = "Cliente X", DiagnosticId = "DIAG-1" },
                Manual = new ManualData { MachineLabel = "ADM-04", Responsible = "Maria", Department = "Administrativo" },
                StartedAt = DateTimeOffset.Now,
                FinishedAt = DateTimeOffset.Now
            };
        }

        /// <summary>Reconstrói a entrada do Reporting a partir de uma fixture já gravada.</summary>
        private static ReportInput InputFrom(JObject fixture)
        {
            var manual = fixture["manual"] as JObject ?? new JObject();
            var client = fixture["client"] as JObject ?? new JObject();
            var execution = fixture["execution"] as JObject ?? new JObject();

            return new ReportInput
            {
                Identification = new Identification
                {
                    Technician = (string)execution["technician"],
                    Client = (string)client["name"],
                    Unit = (string)client["unit"],
                    DiagnosticId = (string)execution["diagnosticId"],
                    CorporateEnvironment = (bool?)manual["corporateEnvironment"] ?? false
                },
                Manual = new ManualData
                {
                    MachineLabel = (string)manual["machineLabel"],
                    Responsible = (string)manual["responsible"],
                    Department = (string)manual["department"],
                    PhysicalLocation = (string)manual["physicalLocation"],
                    AssetTag = (string)manual["assetTag"],
                    PhysicalCondition = (string)manual["physicalCondition"],
                    Notes = (string)manual["notes"]
                },
                IsElevated = (bool?)execution["elevated"] ?? false,
                // Conversão pelo token, e não por (string) + Parse: o Json.NET reconhece a
                // data ISO da fixture e guarda um token de DATA, cujo (string) sai no formato
                // invariante enquanto o Parse lê na cultura da máquina. Passa no runner do CI,
                // que é en-US, e quebra em máquina pt-BR — que é onde o projeto é desenvolvido
                // e onde a ferramenta roda.
                StartedAt = (DateTimeOffset)execution["startedAt"],
                FinishedAt = (DateTimeOffset)execution["finishedAt"],
                Collectors = CollectorsFrom(fixture)
            };
        }

        private static IList<CollectorResult> CollectorsFrom(JObject fixture)
        {
            var results = new List<CollectorResult>();
            var collectors = fixture["collectors"] as JArray;
            if (collectors == null) return results;

            foreach (var token in collectors.OfType<JObject>())
            {
                var errors = new List<CollectorError>();
                foreach (var error in (token["errors"] as JArray ?? new JArray()).OfType<JObject>())
                    errors.Add(new CollectorError { Source = (string)error["source"], Message = (string)error["message"] });

                CollectorStatus status;
                if (!Enum.TryParse((string)token["status"], out status)) status = CollectorStatus.Failed;

                results.Add(new CollectorResult
                {
                    Id = (string)token["id"],
                    DisplayName = (string)token["displayName"],
                    Status = status,
                    SkipReason = (string)token["skipReason"],
                    RequiresElevation = (bool?)token["requiresElevation"] ?? false,
                    DurationMs = (long?)token["durationMs"] ?? 0,
                    TimedOut = (bool?)token["timedOut"] ?? false,
                    Summary = (string)token["summary"],
                    Errors = errors,
                    Data = token["data"]
                });
            }

            return results;
        }

        private static JObject LoadFixture(string name)
        {
            var path = Path.Combine(RepositoryLayout.FixturesDirectory, name + ".json");
            var text = File.ReadAllText(path);
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);

            return JObject.Parse(text);
        }
    }
}
