using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace EpicoraCheckup.Rules.Tests
{
    /// <summary>
    /// A matriz tem duas origens desde o ADR-013: a pasta <c>rules/</c> e o recurso embutido
    /// no executável. As duas precisam produzir exatamente a mesma coisa.
    ///
    /// Não é redundância. <c>Score.VerdictDrivenBy</c> preserva a ORDEM DE CARGA — ordinal por
    /// nome de arquivo —, então uma origem que ordene diferente muda o JSON de saída sem
    /// mudar nenhum achado, e os golden files pegariam isso só por acidente.
    /// </summary>
    public sealed class OrigemDaMatrizTests
    {
        [Fact]
        public void Carregar_por_conteudo_reproduz_o_que_a_pasta_produz()
        {
            var daPasta = RuleRepository.LoadFromDirectory(RepositoryLayout.RulesDirectory);
            var doConteudo = RuleRepository.LoadFromFiles(ArquivosDaMatriz());

            Assert.Equal(
                daPasta.Select(r => r.Id).ToList(),
                doConteudo.Select(r => r.Id).ToList());

            Assert.Equal(daPasta.Count, doConteudo.Count);
        }

        [Fact]
        public void Ordem_de_carga_nao_depende_da_ordem_em_que_os_arquivos_chegam()
        {
            // O recurso embutido não garante ordem nenhuma de enumeração — quem ordena é o
            // repositório, e é isso que este teste fixa.
            var normal = RuleRepository.LoadFromFiles(ArquivosDaMatriz());
            var invertida = RuleRepository.LoadFromFiles(ArquivosDaMatriz().Reverse());

            Assert.Equal(normal.Select(r => r.Id).ToList(), invertida.Select(r => r.Id).ToList());
        }

        [Fact]
        public void Arquivos_de_apoio_nao_entram_como_regra()
        {
            // event-ids, windows-builds, win11-cpu-support e startup-exclusions são tabelas
            // consumidas pelos coletores. Se entrassem como categoria, o carregamento
            // falharia por não ter a lista "rules" — ou pior, entrariam vazias.
            var rules = RuleRepository.LoadFromFiles(ArquivosDaMatriz());

            Assert.Equal(61, rules.Count);
            Assert.All(rules, rule => Assert.False(string.IsNullOrWhiteSpace(rule.Id)));
        }

        private static IList<KeyValuePair<string, string>> ArquivosDaMatriz()
        {
            return Directory.GetFiles(RepositoryLayout.RulesDirectory, "*.json")
                .Select(path => new KeyValuePair<string, string>(
                    Path.GetFileName(path), File.ReadAllText(path)))
                .ToList();
        }
    }
}
