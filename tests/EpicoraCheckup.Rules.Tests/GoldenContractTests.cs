using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EpicoraCheckup.Rules.Tests
{
    /// <summary>
    /// O contrato de aceite do motor C#.
    ///
    /// tests/README.md: "o motor C# tem que produzir exatamente isto. Quando passar nos
    /// três, o motor Node é aposentado — ele é instrumento, não segundo sistema."
    ///
    /// São dois contratos por fixture, e os dois importam:
    ///
    ///  1. Só as regras habilitadas — o que a ferramenta produz de verdade hoje. A saída
    ///     esperada é o próprio bloco findings/score gravado dentro da fixture.
    ///  2. A matriz inteira, incluindo as 56 pendentes — tests/expected/*.matriz-completa.json.
    ///     Pega regressão em regra que ainda não foi habilitada, que é justamente onde
    ///     ninguém olharia.
    /// </summary>
    public sealed class GoldenContractTests
    {
        public static IEnumerable<object[]> Fixtures => new[]
        {
            new object[] { "sintetica-verde" },
            new object[] { "sintetica-amarela" },
            new object[] { "sintetica-vermelha" }
        };

        [Theory]
        [MemberData(nameof(Fixtures))]
        public void Apenas_regras_habilitadas_reproduz_o_bloco_da_fixture(string fixture)
        {
            var document = LoadFixture(fixture);

            var esperado = new JObject
            {
                ["findings"] = document["findings"],
                ["score"] = document["score"]
            };

            AssertMatches(esperado, Evaluate(document, includePending: false), fixture, "regras habilitadas");
        }

        [Theory]
        [MemberData(nameof(Fixtures))]
        public void Matriz_completa_reproduz_o_golden_file(string fixture)
        {
            var document = LoadFixture(fixture);
            var esperado = LoadJson(Path.Combine(RepositoryLayout.ExpectedDirectory, fixture + ".matriz-completa.json"));

            AssertMatches(esperado, Evaluate(document, includePending: true), fixture, "matriz completa");
        }

        [Fact]
        public void A_matriz_carrega_inteira_e_sem_id_duplicado()
        {
            var rules = RuleRepository.LoadFromDirectory(RepositoryLayout.RulesDirectory);

            // Se este número mudar, foi porque alguém acrescentou ou removeu regra.
            // Regra nunca é deletada: marca-se enabled false. Então só deve subir.
            Assert.Equal(61, rules.Count);
            Assert.Equal(rules.Count, rules.Select(r => r.Id).Distinct().Count());

            // Toda regra precisa de condição: regra sem condition nunca dispara e passaria
            // como "conforme" para sempre, silenciosamente.
            Assert.All(rules, rule => Assert.NotNull(rule.Condition));
        }

        [Fact]
        public void Regra_habilitada_tem_clientText_aprovado()
        {
            var rules = RuleRepository.LoadFromDirectory(RepositoryLayout.RulesDirectory);

            // Regra de contribuição 4, não negociável: regra sem clientText aprovado pelo
            // comercial não entra em release. Aqui isso deixa de ser combinado e passa a
            // ser verificado.
            var semTexto = rules
                .Where(r => r.Enabled && string.IsNullOrWhiteSpace(r.ClientText))
                .Select(r => r.Id)
                .ToList();

            Assert.True(semTexto.Count == 0,
                "regra habilitada sem clientText: " + string.Join(", ", semTexto));
        }

        private static JObject Evaluate(JObject document, bool includePending)
        {
            var rules = RuleRepository.LoadFromDirectory(RepositoryLayout.RulesDirectory);
            var evaluation = new RuleEngine(rules).Evaluate(document, includePending);

            // Compara a forma SERIALIZADA, não os objetos: o que o consolidador lê é o
            // JSON, então é o JSON que precisa bater — incluindo camelCase, enum como
            // texto e null explícito.
            return JObject.Parse(CheckupJson.Serialize(evaluation.Result));
        }

        private static void AssertMatches(JToken esperado, JToken obtido, string fixture, string cenario)
        {
            if (JToken.DeepEquals(esperado, obtido)) return;

            Assert.True(false,
                $"{fixture} [{cenario}]: a saída do motor divergiu do esperado.\n\n" +
                PrimeiraDiferenca(esperado, obtido));
        }

        /// <summary>
        /// DeepEquals responde sim ou não, o que não ajuda a corrigir. Isto aponta o
        /// primeiro achado que difere, que é onde normalmente está a causa.
        /// </summary>
        private static string PrimeiraDiferenca(JToken esperado, JToken obtido)
        {
            var esperadosFindings = esperado["findings"] as JArray ?? new JArray();
            var obtidosFindings = obtido["findings"] as JArray ?? new JArray();

            if (esperadosFindings.Count != obtidosFindings.Count)
            {
                return $"contagem de achados: esperado {esperadosFindings.Count}, obtido {obtidosFindings.Count}";
            }

            for (var i = 0; i < esperadosFindings.Count; i++)
            {
                if (JToken.DeepEquals(esperadosFindings[i], obtidosFindings[i])) continue;

                return $"primeiro achado divergente, posição {i}:\n\n" +
                       $"--- esperado ---\n{esperadosFindings[i]}\n\n" +
                       $"--- obtido ---\n{obtidosFindings[i]}";
            }

            if (!JToken.DeepEquals(esperado["score"], obtido["score"]))
            {
                return $"score divergente:\n\n--- esperado ---\n{esperado["score"]}\n\n--- obtido ---\n{obtido["score"]}";
            }

            return $"--- esperado ---\n{esperado}\n\n--- obtido ---\n{obtido}";
        }

        private static JObject LoadFixture(string name)
        {
            return LoadJson(Path.Combine(RepositoryLayout.FixturesDirectory, name + ".json"));
        }

        private static JObject LoadJson(string path)
        {
            // JSON gravado em máquina Windows pode vir com BOM, e o parser rejeita.
            var text = File.ReadAllText(path);
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);

            return JObject.Parse(text);
        }
    }
}
