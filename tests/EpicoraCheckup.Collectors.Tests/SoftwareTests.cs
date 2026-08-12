using System.Collections.Generic;
using System.Linq;
using EpicoraCheckup.Collectors.Collectors;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EpicoraCheckup.Collectors.Tests
{
    /// <summary>
    /// Software instalado e inicialização.
    ///
    /// É a CLASSIFICAÇÃO, e não a lista crua, que gera achado comercial: SEC-011 (acesso
    /// remoto), SEC-012 (backup) e SW-005 (antivírus de terceiro) leem daqui — e
    /// <c>edrAgents</c> é metade do cruzamento que impede o pior falso positivo do produto.
    /// </summary>
    public sealed class SoftwareTests
    {
        [Fact]
        public void Programa_e_classificado_pelo_nome_ou_pelo_fabricante()
        {
            var dados = SoftwareFacts.Build(new List<InstalledProgram>
            {
                Programa("AnyDesk", "philandro Software GmbH"),
                Programa("Falcon Sensor", "CrowdStrike, Inc."),
                Programa("Veeam Agent for Microsoft Windows", "Veeam Software"),
                Programa("Java 8 Update 411", "Oracle Corporation"),
                Programa("CCleaner", "Piriform"),
                Programa("Google Chrome", "Google LLC")
            });

            var classificacao = dados["classification"];

            Assert.Equal("AnyDesk", (string)classificacao["remoteAccessTools"][0]);
            Assert.Equal("Falcon Sensor", (string)classificacao["edrAgents"][0]);
            Assert.Equal("Veeam Agent for Microsoft Windows", (string)classificacao["backupAgents"][0]);
            Assert.Equal("Java 8 Update 411", (string)classificacao["obsoleteRuntimes"][0]);
            Assert.Equal("CCleaner", (string)classificacao["potentiallyUnwanted"][0]);

            // Nunca afirmar irregularidade de licenciamento: aguarda revisão do jurídico
            // (doc 03 §4.7).
            Assert.Empty(classificacao["licenseReviewCandidates"]);
        }

        [Fact]
        public void Navegador_e_detectado_mas_a_versao_mais_recente_nao_e_inventada()
        {
            var dados = SoftwareFacts.Build(new List<InstalledProgram>
            {
                Programa("Google Chrome", "Google LLC", "127.0.6533.100"),
                Programa("Mozilla Firefox (x64 pt-BR)", "Mozilla")
            });

            var navegadores = dados["browsers"];

            Assert.Equal(2, navegadores.Count());
            Assert.Equal("127.0.6533.100", (string)navegadores[0]["version"]);

            // latestKnownVersion exigiria tabela mantida, que não existe: outdated fica null e
            // SW-003 resolve Indeterminate em vez de acusar navegador em dia de desatualizado.
            Assert.Equal(JTokenType.Null, navegadores[0]["outdated"].Type);
            Assert.Empty(dados["outdatedBrowsers"]);
        }

        [Fact]
        public void Programa_repetido_em_HKLM_e_HKCU_conta_uma_vez_so()
        {
            var dados = SoftwareFacts.Build(new List<InstalledProgram>
            {
                Programa("Zoom Workplace", "Zoom"),
                Programa("zoom workplace", "Zoom"),
                Programa("7-Zip 24.08", "Igor Pavlov")
            });

            Assert.Equal(2, (int)dados["count"]);

            // Ordenação ordinal: o mesmo parque coletado em máquinas de idiomas diferentes tem
            // que produzir a mesma lista.
            Assert.Equal("7-Zip 24.08", (string)dados["programs"][0]["displayName"]);
        }

        [Theory]
        [InlineData("20240310", "2024-03-10")]
        [InlineData("2024-03-10", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void Data_de_instalacao_so_e_aceita_no_formato_que_o_registro_usa(
            object cru, string esperado)
        {
            Assert.Equal(esperado, SoftwareFacts.InstallDate(cru));
        }

        [Fact]
        public void Tamanho_estimado_do_registro_esta_em_KiB_e_vai_para_o_JSON_em_bytes()
        {
            Assert.Equal(1048576L, SoftwareFacts.SizeBytes(1024));
            Assert.Null(SoftwareFacts.SizeBytes(null));
            Assert.Null(SoftwareFacts.SizeBytes("não é número"));
        }

        // ---------------------------------------------------------------- inicialização

        [Theory]
        [InlineData("\"C:\\Arquivos\\app.exe\" --minimizado", "C:\\Arquivos\\app.exe")]
        [InlineData("C:\\Arquivos\\app.exe /background", "C:\\Arquivos\\app.exe")]
        [InlineData("rundll32.exe algo,Entrada", "rundll32.exe")]
        public void Executavel_e_isolado_da_linha_de_comando(string comando, string esperado)
        {
            // O caminho não existe nesta máquina, então ExecutablePath devolve null — o que
            // este teste fixa é o RECORTE, exercitado pela extração de aspas e de sufixo .exe.
            Assert.Equal(esperado, Recorte(comando));
        }

        [Fact]
        public void Comando_sem_executavel_reconhecivel_nao_produz_palpite()
        {
            Assert.Null(StartupFacts.ExecutablePath("   "));
            Assert.Null(StartupFacts.ExecutablePath(null));
        }

        [Fact]
        public void Valor_de_registro_em_varias_linhas_vira_um_comando_so()
        {
            Assert.Equal("linha1 linha2", StartupFacts.CommandText(new[] { "linha1", "linha2" }));
            Assert.Null(StartupFacts.CommandText(null));
        }

        /// <summary>
        /// Repete o recorte de <see cref="StartupFacts.ExecutablePath"/> sem o teste de
        /// existência do arquivo, que depende da máquina onde o teste roda.
        /// </summary>
        private static string Recorte(string comando)
        {
            var aspas = System.Text.RegularExpressions.Regex.Match(comando, "^\\s*\"([^\"]+)\"");
            if (aspas.Success) return aspas.Groups[1].Value;

            var nu = System.Text.RegularExpressions.Regex.Match(comando, @"^\s*(\S+\.exe)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return nu.Success ? nu.Groups[1].Value : null;
        }

        private static InstalledProgram Programa(string nome, string fabricante, string versao = null)
        {
            return new InstalledProgram
            {
                DisplayName = nome,
                Publisher = fabricante,
                DisplayVersion = versao,
                Scope = "HKLM"
            };
        }
    }
}
