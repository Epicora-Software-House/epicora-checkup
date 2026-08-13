using System;
using EpicoraCheckup.Core.Update;
using Xunit;

namespace EpicoraCheckup.Rules.Tests
{
    /// <summary>
    /// Verificação de versão (ADR-014, doc 02 §8.3).
    ///
    /// O requisito que mais importa não é acertar a comparação: é **nunca bloquear**. A
    /// verificação roda na máquina de um cliente, com proxy, firewall, EDR e limite de 60
    /// requisições por hora por IP na API não autenticada do GitHub. Todo caminho que não dê
    /// certo tem de terminar em uma linha de log e no diagnóstico seguindo adiante.
    ///
    /// O trecho de resposta usado aqui é o formato real de <c>/releases/latest</c>, cortado
    /// nos campos que importam.
    /// </summary>
    public sealed class VerificacaoDeVersaoTests
    {
        private const string RespostaDaApi = @"{
          ""url"": ""https://api.github.com/repos/Epicora-Software-House/epicora-checkup/releases/1"",
          ""tag_name"": ""v1.2.0"",
          ""name"": ""v1.2.0"",
          ""draft"": false,
          ""prerelease"": false,
          ""assets"": [ { ""name"": ""EpicoraCheckup.exe"" } ]
        }";

        [Fact]
        public void Tag_sai_da_resposta_da_api()
        {
            Assert.Equal("v1.2.0", UpdateCheck.TagOf(RespostaDaApi));
        }

        [Fact]
        public void Versao_publicada_mais_nova_avisa_e_carrega_o_link()
        {
            var resultado = UpdateCheck.Evaluate("1.0.0", RespostaDaApi);

            Assert.Equal(UpdateState.Outdated, resultado.State);
            Assert.Equal("1.0.0", resultado.InstalledVersion);
            Assert.Equal("1.2.0", resultado.LatestVersion);

            // O log é o único registro que sobra de uma verificação que não bloqueia nada, e a
            // URL nele é o que permite guiar o técnico por telefone depois.
            Assert.Contains(UpdateCheck.DownloadUrl, resultado.Detail);
        }

        [Fact]
        public void Versao_igual_a_publicada_nao_avisa()
        {
            var resultado = UpdateCheck.Evaluate("1.2.0", RespostaDaApi);

            Assert.Equal(UpdateState.UpToDate, resultado.State);
        }

        [Fact]
        public void Build_mais_novo_que_o_publicado_nao_avisa()
        {
            // Binário de bancada rodando antes de a tag existir. Avisar aqui mandaria o técnico
            // "atualizar" para uma versão mais velha que a que ele tem na mão.
            var resultado = UpdateCheck.Evaluate("1.3.0", RespostaDaApi);

            Assert.Equal(UpdateState.UpToDate, resultado.State);
            Assert.Contains("não publicado", resultado.Detail);
        }

        [Fact]
        public void Comparacao_e_numerica_e_nao_alfabetica()
        {
            // "1.10.0" < "1.9.0" em ordem alfabética, e é assim que um aviso de versão vira
            // silêncio justamente na versão que mais importa avisar.
            var resultado = UpdateCheck.Evaluate("1.9.0", RespostaDaApi.Replace("v1.2.0", "v1.10.0"));

            Assert.Equal(UpdateState.Outdated, resultado.State);
            Assert.Equal("1.10.0", resultado.LatestVersion);
        }

        [Theory]
        [InlineData("v1.2.0-rc1")]
        [InlineData("v1.2")]
        [InlineData("release-2026-08")]
        [InlineData("1.2.0.4")]
        public void Tag_fora_do_padrao_nao_produz_aviso(string tag)
        {
            var resultado = UpdateCheck.Evaluate("1.0.0", RespostaDaApi.Replace("v1.2.0", tag));

            Assert.Equal(UpdateState.NotChecked, resultado.State);
            Assert.Null(resultado.LatestVersion);
            Assert.Contains(tag, resultado.Detail);
        }

        [Theory]
        // Resposta de repositório sem release publicado ainda — que é o estado do projeto até a
        // primeira tag v*, e não pode virar aviso nenhum na tela.
        [InlineData("{ \"message\": \"Not Found\", \"status\": \"404\" }")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("<html>portal de autenticação do wi-fi do cliente</html>")]
        public void Resposta_sem_tag_nao_produz_aviso(string corpo)
        {
            var resultado = UpdateCheck.Evaluate("1.0.0", corpo);

            Assert.Equal(UpdateState.NotChecked, resultado.State);
        }

        [Fact]
        public void Versao_instalada_ilegivel_nao_produz_aviso()
        {
            var resultado = UpdateCheck.Evaluate("desconhecida", RespostaDaApi);

            Assert.Equal(UpdateState.NotChecked, resultado.State);
        }

        [Fact]
        public void Falha_na_consulta_nunca_propaga()
        {
            // Este é o teste do requisito do doc 02 §8.3. Sem rede, com proxy que recusa, no
            // limite de requisição — o desfecho é sempre o mesmo, e nunca é exceção.
            var resultado = UpdateCheck.Run("1.0.0", () =>
            {
                throw new InvalidOperationException("O servidor remoto retornou um erro: (403) Proibido.");
            });

            Assert.Equal(UpdateState.NotChecked, resultado.State);
            Assert.Contains("403", resultado.Detail);
        }

        [Fact]
        public void Mensagem_de_erro_de_varias_linhas_vira_uma_linha_de_log()
        {
            // RunLog escreve uma linha por registro. Uma mensagem com quebra de linha
            // arrebentaria o formato do arquivo que vai no pacote de entrega interna.
            var resultado = UpdateCheck.Run("1.0.0", () =>
            {
                throw new InvalidOperationException("primeira linha\r\nsegunda linha\nterceira");
            });

            Assert.DoesNotContain("\n", resultado.Detail);
            Assert.DoesNotContain("\r", resultado.Detail);
        }

        [Fact]
        public void Consulta_bem_sucedida_compara_o_que_veio()
        {
            var resultado = UpdateCheck.Run("1.0.0", () => RespostaDaApi);

            Assert.Equal(UpdateState.Outdated, resultado.State);
        }

        [Fact]
        public void A_url_de_download_tem_nome_de_asset_fixo()
        {
            // Doc 02 §8.1: a URL estável só resolve para o binário mais recente se o nome do
            // asset não carregar número de versão. Um "EpicoraCheckup-1.2.0.exe" quebraria o
            // link que o técnico recebe por telefone, e o aviso desta tela aponta para ele.
            Assert.EndsWith("/releases/latest/download/EpicoraCheckup.exe", UpdateCheck.DownloadUrl);
            Assert.Contains(UpdateCheck.RepositorySlug, UpdateCheck.LatestReleaseUrl);
        }
    }
}
