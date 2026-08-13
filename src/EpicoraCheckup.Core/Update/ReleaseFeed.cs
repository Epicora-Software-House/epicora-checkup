using System;
using System.IO;
using System.Net;
using System.Text;

namespace EpicoraCheckup.Core.Update
{
    /// <summary>
    /// A fonte da verificação de versão: uma requisição HTTP à API de releases do GitHub.
    ///
    /// **Sem teste automatizado, de propósito** — é a mesma separação dos coletores. Fonte se
    /// exercita em campo; o que tem teste é a decisão, em <see cref="UpdateCheck"/>. Aqui não
    /// há decisão nenhuma: devolve texto ou lança, e quem chama trata as duas coisas igual.
    ///
    /// Detalhes que não são cosméticos:
    ///
    ///  - **User-Agent é obrigatório.** A API do GitHub responde 403 a requisição sem ele.
    ///  - **Proxy com credencial de máquina.** Rede de cliente corporativo com proxy
    ///    autenticado é comum, e sem isto a verificação falharia em todo cliente que tem um.
    ///  - **Tempo limite dos dois lados.** <c>Timeout</c> cobre estabelecer a resposta,
    ///    <c>ReadWriteTimeout</c> cobre a leitura do corpo. Só o primeiro deixaria a leitura
    ///    de um corpo lento pendurada além dos 3 segundos do doc 02 §8.3.
    /// </summary>
    public static class ReleaseFeed
    {
        /// <summary>
        /// Teto de leitura do corpo. A resposta real tem dezenas de KB e o <c>tag_name</c> vem
        /// no começo; o teto existe para que uma resposta inesperadamente enorme não vire
        /// consumo de memória numa máquina de cliente.
        /// </summary>
        private const int MaxBytes = 128 * 1024;

        public static string Fetch(string url, TimeSpan timeout)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);

            request.Method = "GET";
            request.Timeout = (int)timeout.TotalMilliseconds;
            request.ReadWriteTimeout = (int)timeout.TotalMilliseconds;
            request.UserAgent = "EpicoraCheckup";
            request.Accept = "application/vnd.github+json";

            // Nada é cacheado do lado da ferramenta, mas proxy intermediário pode servir
            // resposta velha — que é justamente o que tornaria o aviso de versão inútil.
            request.CachePolicy = new System.Net.Cache.RequestCachePolicy(
                System.Net.Cache.RequestCacheLevel.NoCacheNoStore);

            if (request.Proxy != null)
                request.Proxy.Credentials = CredentialCache.DefaultCredentials;

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            {
                if (stream == null) return string.Empty;

                return ReadCapped(stream);
            }
        }

        private static string ReadCapped(Stream stream)
        {
            using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
            {
                var buffer = new char[8192];
                var text = new StringBuilder();

                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    text.Append(buffer, 0, read);

                    if (text.Length >= MaxBytes) break;
                }

                return text.ToString();
            }
        }
    }
}
