using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace EpicoraCheckup.Rules.Tests
{
    /// <summary>
    /// A versão da matriz (ADR-015) é o que amarra um número contestado a um conteúdo de
    /// matriz. Data declarada em <c>matriz.json</c> mais impressão digital do conteúdo
    /// carregado: a data é rótulo, a impressão digital é fato.
    ///
    /// O que estes testes protegem é a segunda parte. Uma impressão digital que muda quando
    /// nada mudou destrói a confiança no campo tão rápido quanto uma que não muda quando
    /// alguém editou uma regra.
    /// </summary>
    public sealed class VersaoDaMatrizTests
    {
        [Fact]
        public void Versao_da_matriz_do_repositorio_tem_data_declarada_e_impressao_digital()
        {
            var versao = RuleRepository.VersionOf(ArquivosDaMatriz());

            Assert.Matches(@"^\d{4}\.\d{2}\.\d{2}\+[0-9a-f]{8}$", versao);
        }

        [Fact]
        public void As_duas_origens_da_matriz_produzem_a_mesma_versao()
        {
            // Mesma exigência do ADR-013 que OrigemDaMatrizTests fixa para as regras: pasta e
            // recurso embutido têm de concordar. Se divergissem aqui, dois relatórios da mesma
            // matriz alegariam versões diferentes — e a auditoria não teria como saber qual.
            var daPasta = RuleRepository.VersionOf(RuleRepository.ReadDirectory(RepositoryLayout.RulesDirectory));
            var doConteudo = RuleRepository.VersionOf(ArquivosDaMatriz());

            Assert.Equal(daPasta, doConteudo);
        }

        [Fact]
        public void Ordem_em_que_os_arquivos_chegam_nao_muda_a_versao()
        {
            // Recurso embutido não garante ordem de enumeração nenhuma.
            var normal = RuleRepository.VersionOf(ArquivosDaMatriz());
            var invertida = RuleRepository.VersionOf(ArquivosDaMatriz().Reverse());

            Assert.Equal(normal, invertida);
        }

        [Fact]
        public void Editar_uma_regra_muda_a_impressao_digital()
        {
            var original = ArquivosDaMatriz();
            var editada = Substituir(original, "storage.json", conteudo => conteudo.Replace("\"weight\": 25", "\"weight\": 24"));

            Assert.NotEqual(RuleRepository.VersionOf(original), RuleRepository.VersionOf(editada));

            // A data declarada continua a mesma: é rótulo, e ninguém a bumpou. O que muda é a
            // metade que não depende de ninguém lembrar de nada.
            Assert.Equal(Declarada(RuleRepository.VersionOf(original)), Declarada(RuleRepository.VersionOf(editada)));
        }

        [Fact]
        public void Mover_uma_regra_de_arquivo_muda_a_impressao_digital()
        {
            // A ordem de carga é ordinal por nome de arquivo e é parte da saída — o
            // Score.VerdictDrivenBy a preserva. Duas matrizes com as mesmas 61 regras em
            // arquivos diferentes não são a mesma matriz.
            var original = ArquivosDaMatriz();

            var renomeada = original
                .Select(a => a.Key == "storage.json"
                    ? new KeyValuePair<string, string>("armazenamento.json", a.Value)
                    : a)
                .ToList();

            Assert.NotEqual(RuleRepository.VersionOf(original), RuleRepository.VersionOf(renomeada));
        }

        [Fact]
        public void Fim_de_linha_de_windows_nao_muda_a_impressao_digital()
        {
            // Este é o caso que tornaria o campo inútil na prática: alguém abre uma regra no
            // Notepad de uma máquina Windows, salva sem mudar nada, e a matriz passa a alegar
            // outra versão.
            var original = ArquivosDaMatriz();
            var comCrLf = original
                .Select(a => new KeyValuePair<string, string>(a.Key, a.Value.Replace("\n", "\r\n")))
                .ToList();

            Assert.Equal(RuleRepository.VersionOf(original), RuleRepository.VersionOf(comCrLf));
        }

        [Fact]
        public void Mexer_em_arquivo_de_apoio_nao_muda_a_versao()
        {
            // event-ids, windows-builds, win11-cpu-support e startup-exclusions alimentam
            // coletor, não matriz: mudam o que a máquina responde, não o critério de avaliação.
            var original = ArquivosDaMatriz();
            var editada = Substituir(original, "event-ids.json", conteudo => conteudo.Replace("\"validUntil\": null", "\"validUntil\": \"2027-01-01\""));

            Assert.Equal(RuleRepository.VersionOf(original), RuleRepository.VersionOf(editada));
        }

        [Fact]
        public void Bumpar_a_data_declarada_muda_a_versao_sem_mexer_na_impressao_digital()
        {
            var original = ArquivosDaMatriz();
            var bumpada = Substituir(original, RuleRepository.VersionFileName,
                conteudo => conteudo.Replace("\"version\": \"2026.08.12\"", "\"version\": \"2099.12.31\""));

            var antes = RuleRepository.VersionOf(original);
            var depois = RuleRepository.VersionOf(bumpada);

            Assert.NotEqual(antes, depois);
            Assert.Equal("2099.12.31", Declarada(depois));
            Assert.Equal(Impressao(antes), Impressao(depois));
        }

        [Fact]
        public void Matriz_sem_declaracao_ainda_tem_versao()
        {
            // Pasta rules/ montada à mão ao lado do executável (ADR-013) não precisa declarar
            // data nenhuma. A impressão digital sozinha ainda responde "qual matriz foi esta".
            var sem = ArquivosDaMatriz().Where(a => a.Key != RuleRepository.VersionFileName).ToList();

            Assert.Matches("^[0-9a-f]{8}$", RuleRepository.VersionOf(sem));
        }

        [Theory]
        [InlineData("{ isto não é json")]
        [InlineData("{}")]
        [InlineData("{ \"version\": \"   \" }")]
        public void Declaracao_ilegivel_ou_vazia_nao_derruba_a_versao(string conteudo)
        {
            // Declaração é conveniência. A matriz em si carregou, e recusar-se a produzir
            // versão por causa do rótulo seria trocar um campo torto por nenhum relatório.
            var arquivos = Substituir(ArquivosDaMatriz(), RuleRepository.VersionFileName, _ => conteudo);

            Assert.Matches("^[0-9a-f]{8}$", RuleRepository.VersionOf(arquivos));
        }

        [Fact]
        public void Declaracao_de_versao_nao_entra_como_regra()
        {
            // matriz.json está na lista de arquivos de apoio. Se saísse dela, o carregamento
            // falharia por não ter a lista "rules" — e a ferramenta não avaliaria nada.
            Assert.Equal(61, RuleRepository.LoadFromFiles(ArquivosDaMatriz()).Count);
        }

        // ------------------------------------------------------------ auxiliares

        private static string Declarada(string versao)
        {
            var partes = versao.Split('+');
            return partes.Length == 2 ? partes[0] : null;
        }

        private static string Impressao(string versao)
        {
            var partes = versao.Split('+');
            return partes[partes.Length - 1];
        }

        private static IList<KeyValuePair<string, string>> Substituir(
            IEnumerable<KeyValuePair<string, string>> arquivos, string nome, Func<string, string> transformar)
        {
            var lista = arquivos
                .Select(a => a.Key == nome
                    ? new KeyValuePair<string, string>(a.Key, transformar(a.Value))
                    : a)
                .ToList();

            // Um teste que "edita" um arquivo cujo trecho não existe mais passaria por
            // acidente, comparando duas vezes a mesma coisa.
            var antes = lista.Single(a => a.Key == nome);
            var origem = arquivos.Single(a => a.Key == nome);

            Assert.NotEqual(origem.Value, antes.Value);

            return lista;
        }

        /// <summary>
        /// Os arquivos do repositório, lidos como o recurso embutido chegaria: nome e conteúdo,
        /// sem ordem garantida. Com fim de linha normalizado, porque o git pode entregar CRLF
        /// na máquina de quem clonar com autocrlf e a comparação de impressão digital entre
        /// dois conjuntos precisa partir do mesmo texto.
        /// </summary>
        private static IList<KeyValuePair<string, string>> ArquivosDaMatriz()
        {
            return Directory.GetFiles(RepositoryLayout.RulesDirectory, "*.json")
                .Select(path => new KeyValuePair<string, string>(
                    Path.GetFileName(path),
                    Regex.Replace(File.ReadAllText(path), "\r\n", "\n")))
                .ToList();
        }
    }
}
