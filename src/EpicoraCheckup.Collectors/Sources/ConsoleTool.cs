using System;
using System.Diagnostics;
using System.IO;

namespace EpicoraCheckup.Collectors.Sources
{
    /// <summary>
    /// Execução de utilitário de linha de comando do próprio Windows, para as poucas
    /// perguntas que não têm resposta em WMI nem no registro — hoje só o <c>fsutil</c>.
    ///
    /// Duas cautelas que não são estilo:
    ///
    ///  1. **Caminho absoluto em <c>%SystemRoot%\System32</c>, nunca o PATH.** A ferramenta roda
    ///     em máquina de cliente, e resolver "fsutil" pelo PATH aceitaria qualquer executável
    ///     de mesmo nome numa pasta anterior da lista.
    ///  2. **Tempo limite próprio.** O tempo limite do orquestrador abandona a thread mas não
    ///     mata o processo filho; sem limite aqui, um utilitário travado ficaria vivo depois de
    ///     a ferramenta ter seguido em frente.
    /// </summary>
    public static class ConsoleTool
    {
        /// <summary>Saída padrão do utilitário, ou <c>null</c> se ele não respondeu a tempo.</summary>
        public static string Run(string executableName, string arguments, int timeoutMs)
        {
            var path = Path.Combine(Environment.SystemDirectory, executableName);
            if (!File.Exists(path)) return null;

            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null) return null;

                // Saída curta e conhecida: ler até o fim antes de esperar não corre risco de
                // encher o buffer do canal, que é o que trava este padrão com saída grande.
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();

                if (!process.WaitForExit(timeoutMs))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception)
                    {
                        // Já morreu entre a espera e o Kill. Sem consequência.
                    }

                    return null;
                }

                return string.IsNullOrEmpty(output) ? error : output;
            }
        }
    }
}
