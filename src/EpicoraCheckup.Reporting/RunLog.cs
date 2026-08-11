using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace EpicoraCheckup.Reporting
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error
    }

    /// <summary>
    /// Log de execução, um arquivo por rodada (doc 02 §9).
    ///
    /// **Acumula em memória e grava no fim.** O nome do arquivo é
    /// <c>HOSTNAME_SERIAL_AAAAMMDD</c>, e o hostname só se conhece depois que o coletor
    /// <c>machine</c> respondeu — então não há como abrir o arquivo antes de coletar. O
    /// custo é que uma queda do processo leva o log junto; o benefício é que nada toca o
    /// disco enquanto a ferramenta está em modo somente-leitura, o que é o princípio 1.
    ///
    /// **Nunca registrar** (doc 02 §9): nome de arquivo de usuário, conteúdo de arquivo,
    /// credencial, chave de produto, ou dado pessoal além do que o bloco manual já autoriza.
    /// Quem chama é responsável por isso — o log não tem como saber o que recebeu.
    ///
    /// O log vai no pacote de entrega interna, não para o cliente.
    /// </summary>
    public sealed class RunLog
    {
        private readonly List<string> _lines = new List<string>();

        public void Debug(string message) => Write(LogLevel.Debug, message);

        public void Info(string message) => Write(LogLevel.Info, message);

        public void Warn(string message) => Write(LogLevel.Warn, message);

        public void Error(string message) => Write(LogLevel.Error, message);

        /// <summary>Erro com stack trace. O stack fica no log e nunca no JSON do cliente.</summary>
        public void Error(string message, Exception exception)
        {
            Write(LogLevel.Error, message);
            if (exception != null) Write(LogLevel.Error, exception.ToString());
        }

        public void Write(LogLevel level, string message)
        {
            var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
            _lines.Add($"{stamp} [{level.ToString().ToUpperInvariant(),-5}] {message}");
        }

        public int Count => _lines.Count;

        /// <summary>Grava e devolve o caminho. UTF-8 sem BOM, como todo texto de saída.</summary>
        public string SaveTo(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllLines(path, _lines, new UTF8Encoding(false));
            return path;
        }
    }
}
