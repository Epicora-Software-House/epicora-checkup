using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Reporting
{
    /// <summary>Grava o documento do schema 1.0.</summary>
    public static class JsonReportWriter
    {
        /// <summary>
        /// UTF-8 **sem BOM**.
        ///
        /// Não é preferência: parser de JSON rejeita BOM, e o consolidador teria de limpar o
        /// arquivo antes de ler. O protótipo PowerShell já grava assim, e as duas saídas
        /// precisam ser intercambiáveis (ADR-009). Os `.ps1` é que levam BOM, para o
        /// PowerShell 5.1 ler acentuação — é o oposto, e confundir os dois já custou um commit.
        /// </summary>
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string Write(JObject document, string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(path, document.ToString(Formatting.Indented), Utf8NoBom);
            return path;
        }
    }
}
