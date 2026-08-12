using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace EpicoraCheckup.Reporting
{
    /// <summary>Os arquivos que uma execução deixou em disco.</summary>
    public sealed class ReportFiles
    {
        public string Directory { get; set; }

        public string Json { get; set; }

        public string Html { get; set; }

        public string Log { get; set; }

        /// <summary>O que não deu certo sem impedir a entrega. Vai para o log e para a tela 7.</summary>
        public IList<string> Warnings { get; } = new List<string>();

        public IList<string> All
        {
            get
            {
                var files = new List<string>();

                if (Json != null) files.Add(Json);
                if (Html != null) files.Add(Html);
                if (Log != null) files.Add(Log);

                return files;
            }
        }
    }

    /// <summary>
    /// Grava a saída em <c>&lt;pasta&gt;\&lt;CLIENTE&gt;\</c>, ao lado do executável (doc 01 §8).
    ///
    /// **Nada é escrito fora da pasta de saída** — regra 3 de contribuição, e até a Fase 5
    /// nada é escrito fora dela nem com autorização.
    ///
    /// Ordem de gravação: JSON primeiro. Ele é a fonte única de verdade e o insumo do
    /// consolidador; HTML e log são derivados dele e de dado que já está em memória. Se o
    /// disco estiver cheio ou a pasta protegida, é o JSON que precisa existir.
    /// </summary>
    public static class ReportWriter
    {
        public static ReportFiles Write(CheckupRun run, string outputDirectory)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentNullException(nameof(outputDirectory));

            var files = new ReportFiles
            {
                Directory = Path.Combine(outputDirectory, SafeName(run.ClientName, "CLIENTE"))
            };

            System.IO.Directory.CreateDirectory(files.Directory);

            var document = CheckupDocument.Build(run);
            var baseName = BaseName(run);

            files.Json = Unique(files.Directory, baseName, ".json");
            WriteText(files.Json, document.ToString(Formatting.Indented));

            // A partir daqui, falha vira aviso: com o JSON em disco o diagnóstico está salvo, e
            // interromper deixaria o técnico sem saber o que existe e o que não existe.
            try
            {
                files.Html = Path.ChangeExtension(files.Json, ".html");
                WriteText(files.Html, HtmlReport.Build(run, document));
            }
            catch (Exception exception)
            {
                files.Html = null;
                files.Warnings.Add("não foi possível gravar o relatório HTML: " + exception.Message);
            }

            try
            {
                files.Log = Path.ChangeExtension(files.Json, ".log");
                WriteText(files.Log, RunLog.Build(run, files.Warnings));
            }
            catch (Exception exception)
            {
                files.Log = null;
                files.Warnings.Add("não foi possível gravar o log: " + exception.Message);
            }

            return files;
        }

        /// <summary>
        /// <c>HOSTNAME_SERIAL_AAAAMMDD</c> (doc 02 §5), com os dois primeiros sanitizados e com
        /// fallback determinístico — serial vem vazio ou só com espaços em muitos fabricantes.
        /// </summary>
        public static string BaseName(CheckupRun run)
        {
            return string.Format("{0}_{1}_{2}",
                SafeName(CheckupDocument.Hostname(run), "HOST"),
                SafeName(CheckupDocument.ProductSerial(run), "SEM-SERIAL"),
                run.FinishedAt.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        }

        private static readonly Regex Invalidos = new Regex(@"[\\/:*?""<>|\s]+", RegexOptions.CultureInvariant);

        public static string SafeName(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;

            var clean = Invalidos.Replace(value, "-").Trim('-');
            if (clean.Length == 0) return fallback;

            // Nome de arquivo longo somado a caminho profundo estoura o limite do Windows, e o
            // erro aparece só na máquina do cliente, no fim da visita.
            return clean.Length > 40 ? clean.Substring(0, 40) : clean;
        }

        /// <summary>
        /// Caminho que ainda não existe.
        ///
        /// O nome do doc 02 §5 tem a data, não a hora: duas coletas na mesma máquina no mesmo
        /// dia — que é o caso normal quando se corrige algo e se roda de novo — cairiam no
        /// mesmo arquivo. Sobrescrever apagaria a evidência do estado ANTERIOR, que é
        /// justamente o que prova o que foi feito. O consolidador deduplica por UUID mantendo
        /// a mais recente, então o arquivo extra não atrapalha lá.
        /// </summary>
        private static string Unique(string directory, string baseName, string extension)
        {
            var candidate = Path.Combine(directory, baseName + extension);

            for (var index = 2; File.Exists(candidate) && index < 100; index++)
                candidate = Path.Combine(directory, baseName + "_" + index + extension);

            return candidate;
        }

        /// <summary>
        /// UTF-8 **sem BOM**.
        ///
        /// O BOM no início do arquivo faz <c>JSON.parse</c> falhar, e o consolidador e as
        /// ferramentas Node não conseguiriam ler a saída. A RFC 8259 também diz que
        /// implementações não devem acrescentar BOM a JSON. O protótipo tem a mesma nota, pelo
        /// mesmo motivo — lá o cuidado é não usar <c>Set-Content -Encoding UTF8</c>.
        /// </summary>
        private static void WriteText(string path, string content)
        {
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
    }
}
