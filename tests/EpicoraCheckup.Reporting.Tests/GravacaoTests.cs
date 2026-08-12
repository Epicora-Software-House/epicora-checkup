using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EpicoraCheckup.Reporting.Tests
{
    /// <summary>
    /// A gravação da saída (doc 01 §8).
    ///
    /// O que se verifica é o contrato do arquivo, não a estética do relatório: JSON fora do
    /// schema 1.0 não entra no consolidador, e o erro só apareceria semanas depois, no
    /// escritório, com a visita encerrada e a máquina longe.
    /// </summary>
    public sealed class GravacaoTests
    {
        [Fact]
        public void Grava_os_tres_arquivos_com_o_nome_do_documento_tecnico()
        {
            var pasta = Repositorio.PastaTemporaria();

            var arquivos = ReportWriter.Write(Repositorio.Execucao(), pasta);

            Assert.True(File.Exists(arquivos.Json));
            Assert.True(File.Exists(arquivos.Html));
            Assert.True(File.Exists(arquivos.Log));

            // .\EpicoraCheckup\<CLIENTE>\ — a saída é agrupada por cliente, e é o que separa
            // duas visitas feitas no mesmo dia com o mesmo pen drive.
            Assert.Equal("Cliente-Exemplo", new DirectoryInfo(arquivos.Directory).Name);

            // HOSTNAME_SERIAL_AAAAMMDD, com os dois primeiros sanitizados.
            Assert.Matches(@"^[^_]+_[^_]+_\d{8}\.json$", Path.GetFileName(arquivos.Json));
        }

        [Fact]
        public void JSON_sai_sem_BOM_porque_BOM_quebra_o_consolidador()
        {
            // Set-Content -Encoding UTF8 do PowerShell 5.1 grava COM BOM, e JSON.parse rejeita.
            // A RFC 8259 também diz que implementações não devem acrescentar BOM a JSON.
            var pasta = Repositorio.PastaTemporaria();
            var arquivos = ReportWriter.Write(Repositorio.Execucao(), pasta);

            var bytes = File.ReadAllBytes(arquivos.Json);

            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
            Assert.Equal('{', (char)bytes[0]);
        }

        [Fact]
        public void Documento_tem_exatamente_os_blocos_do_schema()
        {
            var documento = CheckupDocument.Build(Repositorio.Execucao());

            // additionalProperties: false no schema — uma chave a mais reprova o arquivo
            // inteiro, e o consolidador nem chega a ler.
            Assert.Equal(
                new[] { "schemaVersion", "tool", "execution", "client", "manual", "collectors", "findings", "score", "optimization" },
                documento.Properties().Select(p => p.Name).ToArray());

            Assert.Equal("1.0", (string)documento["schemaVersion"]);
            Assert.Equal("EpicoraCheckup", (string)documento["tool"]["name"]);

            // Auditoria de relatório contestado: qual implementação produziu este número.
            Assert.Equal("dotnet", (string)documento["tool"]["runtime"]);

            // Fase 5. Nulo é diferente de "executou e não fez nada".
            Assert.Equal(JTokenType.Null, documento["optimization"].Type);
        }

        [Fact]
        public void Datas_saem_em_ISO_8601_com_offset_de_fuso()
        {
            // Hora local sem offset é ambígua no consolidador, que junta máquinas de fusos
            // diferentes — e o schema rejeita.
            var documento = CheckupDocument.Build(Repositorio.Execucao());

            var padrao = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?([+-]\d{2}:\d{2}|Z)$");

            Assert.Matches(padrao, (string)documento["execution"]["startedAt"]);
            Assert.Matches(padrao, (string)documento["execution"]["finishedAt"]);
            Assert.Equal(72, (int)documento["execution"]["durationSeconds"]);
        }

        [Fact]
        public void Coletores_e_achados_chegam_inteiros_ao_documento()
        {
            var execucao = Repositorio.Execucao();
            var documento = CheckupDocument.Build(execucao);

            Assert.Equal(execucao.Collectors.Count, ((JArray)documento["collectors"]).Count);
            Assert.Equal(execucao.Findings.Count, ((JArray)documento["findings"]).Count);
            Assert.Equal(execucao.Score.Value, (int)documento["score"]["value"]);

            // A fixture vermelha é a máquina ruim do acervo. Se o motor parar de avaliar, o
            // relatório sairia com score cheio e ninguém notaria olhando o arquivo. O valor
            // exato não entra aqui de propósito: ele muda a cada regra habilitada, e o teste
            // viraria manutenção da matriz em vez de contrato da gravação.
            Assert.True((int)documento["score"]["value"] < 100);
            Assert.Equal("Red", (string)documento["score"]["band"]);
        }

        [Fact]
        public void Campo_opcional_vazio_vira_null_e_nunca_string_vazia()
        {
            // Nunca zero, nunca string vazia, nunca "N/A" — isso destrói a análise no
            // consolidador (doc 02 §5).
            var execucao = Repositorio.Execucao();
            execucao.ClientUnit = "   ";
            execucao.AssetTag = null;

            var documento = CheckupDocument.Build(execucao);

            Assert.Equal(JTokenType.Null, documento["client"]["unit"].Type);
            Assert.Equal(JTokenType.Null, documento["manual"]["assetTag"].Type);
        }

        [Fact]
        public void Campo_obrigatorio_em_branco_ganha_texto_em_vez_de_reprovar_o_arquivo()
        {
            // O schema exige minLength 1 em technician, responsible e department. A tela 4 já
            // obriga, mas gravar arquivo inválido no fim de uma visita é o pior desfecho: o
            // dado existe e o consolidador recusa.
            var execucao = Repositorio.Execucao();
            execucao.Responsible = "  ";
            execucao.Department = null;

            var documento = CheckupDocument.Build(execucao);

            Assert.Equal("não informado", (string)documento["manual"]["responsible"]);
            Assert.Equal("não informado", (string)documento["manual"]["department"]);
        }

        [Theory]
        [InlineData("0.1.0.0", "0.1.0")]
        [InlineData("1.2.3+9f8e7d6", "1.2.3")]
        [InlineData("0.1.0", "0.1.0")]
        [InlineData("versão de teste", "0.0.0")]
        [InlineData(null, "0.0.0")]
        public void Versao_da_ferramenta_e_normalizada_para_o_formato_do_schema(string cru, string esperado)
        {
            Assert.Equal(esperado, CheckupDocument.Version(cru));
        }

        [Theory]
        [InlineData("Cliente / Filial: São Paulo", "Cliente-Filial-São-Paulo")]
        [InlineData("   ", "PADRAO")]
        [InlineData(null, "PADRAO")]
        [InlineData("///", "PADRAO")]
        public void Nome_de_arquivo_e_sanitizado_com_fallback_deterministico(string cru, string esperado)
        {
            // Serial vem vazio ou só com espaços em muitos fabricantes (doc 02 §5).
            Assert.Equal(esperado, ReportWriter.SafeName(cru, "PADRAO"));
        }

        [Fact]
        public void Nome_muito_longo_e_truncado_para_nao_estourar_o_caminho_do_Windows()
        {
            Assert.Equal(40, ReportWriter.SafeName(new string('a', 120), "PADRAO").Length);
        }

        [Fact]
        public void Segunda_coleta_no_mesmo_dia_nao_sobrescreve_a_primeira()
        {
            // O nome do doc 02 §5 tem data, não hora. Rodar de novo depois de corrigir algo é o
            // caso normal, e sobrescrever apagaria a evidência do estado anterior.
            var pasta = Repositorio.PastaTemporaria();
            var execucao = Repositorio.Execucao();

            var primeiro = ReportWriter.Write(execucao, pasta);
            var segundo = ReportWriter.Write(execucao, pasta);

            Assert.NotEqual(primeiro.Json, segundo.Json);
            Assert.True(File.Exists(primeiro.Json));
            Assert.True(File.Exists(segundo.Json));
        }

        // ------------------------------------------------------------ relatório HTML

        [Fact]
        public void HTML_e_autocontido_e_imprimivel()
        {
            var execucao = Repositorio.Execucao();
            var html = HtmlReport.Build(execucao, CheckupDocument.Build(execucao));

            // Precisa abrir em máquina sem internet e continuar legível daqui a cinco anos.
            Assert.DoesNotContain("http://", html);
            Assert.DoesNotContain("https://", html);
            Assert.DoesNotContain("<script", html);
            Assert.Contains("@media print", html);
            Assert.Contains("<meta charset=\"utf-8\">", html);
        }

        [Fact]
        public void HTML_escapa_o_que_veio_de_fora()
        {
            // Nome de máquina, observação do técnico e nome de programa entram no relatório e
            // vêm de fora. Um "<" no meio quebraria a página que o cliente recebe.
            var execucao = Repositorio.Execucao();
            execucao.Notes = "usuário relatou <lentidão> & \"travamentos\"";
            execucao.MachineLabel = "<script>alert(1)</script>";

            var html = HtmlReport.Build(execucao, CheckupDocument.Build(execucao));

            Assert.DoesNotContain("<script>alert(1)</script>", html);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
            Assert.Contains("&lt;lentidão&gt; &amp; &quot;travamentos&quot;", html);
        }

        [Fact]
        public void HTML_separa_risco_de_nao_verificado()
        {
            // Matriz completa: hoje só 5 das 61 regras estão habilitadas e nenhuma delas
            // resolve Indeterminate nesta fixture, então o bloco não sairia — por falta de
            // regra habilitada, não por falta de código. O que se testa aqui é a separação.
            var execucao = Repositorio.Execucao(matrizCompleta: true);
            var html = HtmlReport.Build(execucao, CheckupDocument.Build(execucao));

            Assert.Contains("Riscos e pontos de atenção", html);
            Assert.Contains("Não foi possível verificar", html);

            // A frase que impede a leitura errada do bloco: não é problema encontrado.
            Assert.Contains("<strong>Não são problemas encontrados</strong>", html);
        }

        [Fact]
        public void Achado_contestado_pelo_tecnico_sai_da_lista_de_riscos()
        {
            var execucao = Repositorio.Execucao();

            var achado = execucao.Findings.First(f => f.State == Core.Model.RuleState.NonCompliant);
            achado.MarkedFalsePositive = true;
            achado.FalsePositiveJustification = "disco já trocado nesta visita";

            var html = HtmlReport.Build(execucao, CheckupDocument.Build(execucao));

            var riscos = html.IndexOf("Riscos e pontos de atenção", StringComparison.Ordinal);
            var contestados = html.IndexOf("Achados contestados pelo técnico", StringComparison.Ordinal);

            Assert.True(contestados > riscos);
            Assert.Contains("disco já trocado nesta visita", html);

            // O achado continua no JSON: é assim que a regra é corrigida (doc 03 §6).
            var documento = CheckupDocument.Build(execucao);
            var marcados = ((JArray)documento["findings"]).Count(f => (bool?)f["markedFalsePositive"] == true);

            Assert.Equal(1, marcados);
        }

        [Fact]
        public void Inventario_traduz_o_dado_bruto_para_quem_vai_ler()
        {
            // O JSON escreve booleano como "true" e número com ponto decimal. Sair assim no
            // relatório — "SMBv1: true", "10.0% livre" — é o dado cru vazando para a página
            // que o cliente recebe.
            var execucao = Repositorio.Execucao();
            var html = HtmlReport.Build(execucao, CheckupDocument.Build(execucao));

            Assert.DoesNotContain(">true<", html);
            Assert.DoesNotContain(">false<", html);

            // A fixture vermelha tem SMBv1 ligado, HD mecânico e 4% livre no volume de sistema.
            Assert.Contains("<th>SMBv1</th><td>sim</td>", html);
            Assert.Contains("HD mecânico", html);
            Assert.Contains("4%", html);
        }

        [Fact]
        public void Relatorio_diz_quando_a_coleta_rodou_sem_privilegio()
        {
            var execucao = Repositorio.Execucao();
            execucao.Elevated = false;

            var html = HtmlReport.Build(execucao, CheckupDocument.Build(execucao));

            Assert.Contains("sem privilégio de administrador", html);
        }

        // ------------------------------------------------------------ log

        [Fact]
        public void Log_registra_cada_etapa_e_guarda_o_detalhe_tecnico()
        {
            var execucao = Repositorio.Execucao();

            execucao.Collectors[0].Errors.Add(new Core.Contracts.CollectorError
            {
                Source = "Win32_BaseBoard",
                Message = "classe não encontrada",
                Detail = "System.Management.ManagementException:\r\n   em algum lugar"
            });

            var log = RunLog.Build(execucao);

            Assert.Contains("coletor machine terminou", log);
            Assert.Contains("Win32_BaseBoard · classe não encontrada", log);

            // Pilha em várias linhas dentro de um log de uma linha por evento vira texto
            // impossível de filtrar com findstr, que é a ferramenta que existe na máquina.
            Assert.DoesNotContain("System.Management.ManagementException:\r\n", log);
            Assert.Contains("System.Management.ManagementException: |    em algum lugar", log);
        }

        [Fact]
        public void Log_diz_que_a_coleta_rodou_sem_elevacao_e_o_que_isso_custou()
        {
            var execucao = Repositorio.Execucao();
            execucao.Elevated = false;

            Assert.Contains("TPM, BitLocker e SMART não serão lidos", RunLog.Build(execucao));
        }
    }
}
