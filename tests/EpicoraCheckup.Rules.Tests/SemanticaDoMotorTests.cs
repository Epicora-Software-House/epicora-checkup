using System.Collections.Generic;
using EpicoraCheckup.Core.Model;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EpicoraCheckup.Rules.Tests
{
    /// <summary>
    /// Semântica do motor, sobre matriz sintética de uma regra.
    ///
    /// Os golden files provam que o motor reproduz a matriz real de hoje. Estes testes
    /// cobrem o que eles não cobrem: os cantos onde um refactor futuro divergiria em
    /// silêncio, e onde a divergência não seria um número diferente no score — seria a
    /// ferramenta afirmando um problema que não existe na máquina do cliente.
    /// </summary>
    public sealed class SemanticaDoMotorTests
    {
        // ---------------------------------------------------------------- regra número um

        [Fact]
        public void Dado_ausente_em_requires_resolve_Indeterminate_e_nunca_NonCompliant()
        {
            // A condição dispararia se o dado existisse. Ele não existe.
            var finding = Avaliar(
                Documento(new JObject()),
                requires: new[] { "collectors.storage.data.mediaType" },
                condition: Cond("collectors.storage.data.mediaType", "equals", "HDD"));

            Assert.Equal(RuleState.Indeterminate, finding.State);
            Assert.Equal("dado não disponível: collectors.storage.data.mediaType", finding.IndeterminateReason);
        }

        [Fact]
        public void Dado_nulo_explicito_em_requires_tambem_resolve_Indeterminate()
        {
            var finding = Avaliar(
                Documento(new JObject { ["mediaType"] = JValue.CreateNull() }),
                requires: new[] { "collectors.storage.data.mediaType" },
                condition: Cond("collectors.storage.data.mediaType", "equals", "HDD"));

            Assert.Equal(RuleState.Indeterminate, finding.State);
        }

        [Theory]
        [InlineData("Skipped", "ignorado")]
        [InlineData("Failed", "falhou")]
        public void Coletor_que_nao_concluiu_torna_todo_dado_dele_indisponivel(string status, string rotulo)
        {
            var document = Documento(
                data: new JObject { ["mediaType"] = "HDD" },
                status: status,
                skipReason: "sem privilégio");

            var finding = Avaliar(document,
                requires: new[] { "collectors.storage.data.mediaType" },
                condition: Cond("collectors.storage.data.mediaType", "equals", "HDD"));

            // O dado ESTÁ no documento e a condição casaria. O estado do coletor vence.
            Assert.Equal(RuleState.Indeterminate, finding.State);
            Assert.Equal($"coletor \"Armazenamento\" {rotulo} — sem privilégio", finding.IndeterminateReason);
        }

        [Fact]
        public void Sem_skipReason_o_motivo_cai_no_primeiro_erro_do_coletor()
        {
            var document = Documento(
                data: new JObject(),
                status: "Failed",
                skipReason: null,
                errors: new JArray { new JObject { ["message"] = "tempo limite excedido" } });

            var finding = Avaliar(document,
                requires: new[] { "collectors.storage.data.qualquer" },
                condition: Cond("collectors.storage.data.qualquer", "isTrue", null));

            Assert.Equal("coletor \"Armazenamento\" falhou — tempo limite excedido", finding.IndeterminateReason);
        }

        // ---------------------------------------------------------------- o guard

        [Fact]
        public void IndeterminateWhen_impede_que_Unknown_passe_por_notEquals_e_vire_Compliant()
        {
            var data = new JObject { ["mediaType"] = "Unknown" };
            var requires = new[] { "collectors.storage.data.mediaType" };
            var condition = Cond("collectors.storage.data.mediaType", "notEquals", "SSD");

            // Sem o guard: "Unknown" != "SSD" é verdadeiro, então NonCompliant — a
            // ferramenta afirmaria que o disco não é SSD sem saber o que ele é.
            var semGuard = Avaliar(Documento(data), requires, condition);
            Assert.Equal(RuleState.NonCompliant, semGuard.State);

            // Com o guard, resolve Indeterminate e vai para "não foi possível verificar".
            var comGuard = Avaliar(Documento(data), requires, condition,
                indeterminateWhen: Cond("collectors.storage.data.mediaType", "equals", "Unknown"),
                validationNote: "Confirmar valores enumerados em campo. Segunda frase que não deve aparecer.");

            Assert.Equal(RuleState.Indeterminate, comGuard.State);
            Assert.Equal(
                "condição de indeterminação atendida — Confirmar valores enumerados em campo",
                comGuard.IndeterminateReason);
        }

        // ---------------------------------------------------------------- operadores

        [Fact]
        public void Ausente_e_nulo_sao_estados_diferentes_para_equals()
        {
            // Ausente nunca é igual a nada — nem a null.
            Assert.Equal(RuleState.Compliant,
                EstadoDe(new JObject(), Cond("collectors.storage.data.x", "equals", JValue.CreateNull())));

            // Nulo explícito é igual a null.
            Assert.Equal(RuleState.NonCompliant,
                EstadoDe(new JObject { ["x"] = JValue.CreateNull() }, Cond("collectors.storage.data.x", "equals", JValue.CreateNull())));
        }

        [Theory]
        [InlineData("isTrue")]
        [InlineData("isFalse")]
        [InlineData("isEmpty")]
        [InlineData("isNotEmpty")]
        public void Operador_unario_sobre_valor_nulo_nao_afirma_nada(string op)
        {
            // Nenhum destes pode dar verdadeiro sobre ausência: é o que impede
            // "nenhum agente de backup identificado" quando a leitura simplesmente falhou.
            Assert.Equal(RuleState.Compliant, EstadoDe(new JObject(), Cond("collectors.storage.data.x", op, null)));
        }

        [Fact]
        public void notContains_sobre_valor_que_nao_e_texto_nem_lista_devolve_falso()
        {
            // Assimetria deliberada, herdada do motor de referência: não se afirma
            // "não contém" sobre algo que não pôde ser lido.
            Assert.Equal(RuleState.Compliant,
                EstadoDe(new JObject(), Cond("collectors.storage.data.x", "notContains", "algo")));

            Assert.Equal(RuleState.NonCompliant,
                EstadoDe(new JObject { ["x"] = "abc" }, Cond("collectors.storage.data.x", "notContains", "z")));
        }

        [Fact]
        public void Comparacao_numerica_nao_coage_texto()
        {
            // "8" como texto não é 8. Coerção silenciosa aqui viraria achado de RAM errado.
            Assert.Equal(RuleState.Compliant,
                EstadoDe(new JObject { ["gb"] = "8" }, Cond("collectors.storage.data.gb", "lessThan", 16)));

            Assert.Equal(RuleState.NonCompliant,
                EstadoDe(new JObject { ["gb"] = 8 }, Cond("collectors.storage.data.gb", "lessThan", 16)));
        }

        [Fact]
        public void Composicao_allOf_anyOf_e_not()
        {
            var data = new JObject { ["a"] = true, ["b"] = false };

            var allOf = new Condition { AllOf = new List<Condition> { Cond("collectors.storage.data.a", "isTrue", null), Cond("collectors.storage.data.b", "isFalse", null) } };
            Assert.Equal(RuleState.NonCompliant, EstadoDe(data, allOf));

            var anyOf = new Condition { AnyOf = new List<Condition> { Cond("collectors.storage.data.a", "isFalse", null), Cond("collectors.storage.data.b", "isFalse", null) } };
            Assert.Equal(RuleState.NonCompliant, EstadoDe(data, anyOf));

            var not = new Condition { Not = Cond("collectors.storage.data.a", "isTrue", null) };
            Assert.Equal(RuleState.Compliant, EstadoDe(data, not));
        }

        // ---------------------------------------------------------------- score e veredito

        [Theory]
        [InlineData(0, 100, "Green")]
        [InlineData(20, 80, "Green")]
        [InlineData(21, 79, "Yellow")]
        [InlineData(50, 50, "Yellow")]
        [InlineData(51, 49, "Red")]
        public void Faixa_do_semaforo_nos_limites(int peso, int esperado, string faixa)
        {
            var score = AvaliarScore(peso, verdictInfluence: null);

            Assert.Equal(esperado, score.Value);
            Assert.Equal(faixa, score.Band.ToString());
        }

        [Fact]
        public void Score_tem_piso_em_zero()
        {
            Assert.Equal(0, AvaliarScore(250, verdictInfluence: null).Value);
        }

        [Fact]
        public void Indeterminate_nao_pontua_no_score()
        {
            var rule = Regra(
                requires: new[] { "collectors.storage.data.x" },
                condition: Cond("collectors.storage.data.x", "isTrue", null),
                weight: 40);

            var evaluation = new RuleEngine(new List<Rule> { rule }).Evaluate(Documento(new JObject()));

            Assert.Equal(RuleState.Indeterminate, evaluation.Result.Findings[0].State);
            Assert.Equal(100, evaluation.Result.Score.Value);
        }

        [Theory]
        [InlineData("Replace", "Replace")]
        [InlineData("Upgrade", "Upgrade")]
        [InlineData(null, "Keep")]
        public void Veredito_vem_da_influencia_da_regra_que_disparou(string influencia, string esperado)
        {
            Assert.Equal(esperado, AvaliarScore(10, influencia).Verdict.ToString());
        }

        [Fact]
        public void Replace_tem_precedencia_sobre_Upgrade()
        {
            var rules = new List<Rule>
            {
                Regra(condition: Cond("collectors.storage.data.a", "isTrue", null), weight: 5, verdictInfluence: "Upgrade", id: "AAA-001"),
                Regra(condition: Cond("collectors.storage.data.a", "isTrue", null), weight: 5, verdictInfluence: "Replace", id: "ZZZ-001")
            };

            var score = new RuleEngine(rules).Evaluate(Documento(new JObject { ["a"] = true })).Result.Score;

            Assert.Equal(Verdict.Replace, score.Verdict);
            Assert.Equal(new[] { "ZZZ-001" }, score.VerdictDrivenBy);
        }

        [Fact]
        public void Achados_saem_ordenados_por_severidade_e_depois_por_id()
        {
            var rules = new List<Rule>
            {
                Regra(condition: Cond("collectors.storage.data.a", "isTrue", null), id: "BBB-001", severity: Severity.Low),
                Regra(condition: Cond("collectors.storage.data.a", "isTrue", null), id: "ZZZ-001", severity: Severity.Critical),
                Regra(condition: Cond("collectors.storage.data.a", "isTrue", null), id: "AAA-001", severity: Severity.Critical)
            };

            var findings = new RuleEngine(rules).Evaluate(Documento(new JObject { ["a"] = true })).Result.Findings;

            Assert.Equal(new[] { "AAA-001", "ZZZ-001", "BBB-001" }, new[] { findings[0].RuleId, findings[1].RuleId, findings[2].RuleId });
        }

        // ---------------------------------------------------------------- evidência e pendentes

        [Fact]
        public void Evidencia_so_e_coletada_para_NonCompliant_e_ignora_campo_ausente()
        {
            var data = new JObject { ["a"] = true, ["modelo"] = "ST500LM000" };

            var comAchado = Avaliar(Documento(data),
                requires: null,
                condition: Cond("collectors.storage.data.a", "isTrue", null),
                evidenceFields: new[] { "collectors.storage.data.modelo", "collectors.storage.data.naoExiste" });

            Assert.Equal(RuleState.NonCompliant, comAchado.State);
            Assert.Single(comAchado.Evidence);
            Assert.Equal("ST500LM000", comAchado.Evidence["collectors.storage.data.modelo"].ToString());

            var semAchado = Avaliar(Documento(new JObject { ["a"] = false, ["modelo"] = "X" }),
                requires: null,
                condition: Cond("collectors.storage.data.a", "isTrue", null),
                evidenceFields: new[] { "collectors.storage.data.modelo" });

            Assert.Equal(RuleState.Compliant, semAchado.State);
            Assert.Null(semAchado.Evidence);
        }

        [Fact]
        public void Regra_desabilitada_so_entra_com_includePending()
        {
            var rule = Regra(condition: Cond("collectors.storage.data.a", "isTrue", null));
            rule.Enabled = false;

            var engine = new RuleEngine(new List<Rule> { rule });
            var document = Documento(new JObject { ["a"] = true });

            Assert.Empty(engine.Evaluate(document, includePending: false).Result.Findings);
            Assert.Single(engine.Evaluate(document, includePending: true).Result.Findings);
        }

        [Fact]
        public void Comparar_caminho_nulo_fora_de_requires_gera_aviso()
        {
            var rule = Regra(requires: null, condition: Cond("collectors.storage.data.ausente", "equals", "HDD"));

            var evaluation = new RuleEngine(new List<Rule> { rule }).Evaluate(Documento(new JObject()));

            Assert.Single(evaluation.Warnings);
            Assert.Contains("não consta em requires", evaluation.Warnings[0]);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// O parâmetro é JToken, e não object, de propósito: JToken tem conversão
        /// implícita de string/int/bool, e é a única forma de distinguir "a chave value
        /// não existe" (null de C#) de "value é null no JSON" (JValue.CreateNull()).
        /// Os dois dão resultados diferentes em equals, e colapsá-los esconderia isso.
        /// </summary>
        private static Condition Cond(string path, string op, JToken value)
        {
            return new Condition { Path = path, Operator = op, Value = value };
        }

        private static Rule Regra(
            IEnumerable<string> requires = null,
            Condition condition = null,
            Condition indeterminateWhen = null,
            string validationNote = null,
            IEnumerable<string> evidenceFields = null,
            int weight = 10,
            string verdictInfluence = null,
            string id = "TST-001",
            Severity severity = Severity.High)
        {
            return new Rule
            {
                Id = id,
                Version = 1,
                Enabled = true,
                Severity = severity,
                Weight = weight,
                Requires = requires == null ? null : new List<string>(requires),
                IndeterminateWhen = indeterminateWhen,
                Condition = condition,
                Title = "Regra de teste",
                ValidationNote = validationNote,
                EvidenceFields = evidenceFields == null ? null : new List<string>(evidenceFields),
                VerdictInfluence = verdictInfluence
            };
        }

        private static JObject Documento(
            JObject data,
            string status = "Completed",
            string skipReason = null,
            JArray errors = null)
        {
            return new JObject
            {
                ["collectors"] = new JArray
                {
                    new JObject
                    {
                        ["id"] = "storage",
                        ["displayName"] = "Armazenamento",
                        ["status"] = status,
                        ["skipReason"] = skipReason == null ? JValue.CreateNull() : new JValue(skipReason),
                        ["errors"] = errors ?? new JArray(),
                        ["data"] = data
                    }
                }
            };
        }

        private static Finding Avaliar(
            JObject document,
            IEnumerable<string> requires,
            Condition condition,
            Condition indeterminateWhen = null,
            string validationNote = null,
            IEnumerable<string> evidenceFields = null)
        {
            var rule = Regra(requires, condition, indeterminateWhen, validationNote, evidenceFields);
            return new RuleEngine(new List<Rule> { rule }).Evaluate(document).Result.Findings[0];
        }

        private static RuleState EstadoDe(JObject data, Condition condition)
        {
            return Avaliar(Documento(data), null, condition).State;
        }

        private static Score AvaliarScore(int peso, string verdictInfluence)
        {
            var rule = Regra(condition: Cond("collectors.storage.data.a", "isTrue", null), weight: peso, verdictInfluence: verdictInfluence);
            return new RuleEngine(new List<Rule> { rule }).Evaluate(Documento(new JObject { ["a"] = true })).Result.Score;
        }
    }
}
