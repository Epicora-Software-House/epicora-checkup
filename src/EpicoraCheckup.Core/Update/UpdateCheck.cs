using System;
using System.Text.RegularExpressions;

namespace EpicoraCheckup.Core.Update
{
    /// <summary>Desfecho da verificação de versão. Nenhum deles impede a execução.</summary>
    public enum UpdateState
    {
        /// <summary>Esta cópia é a mais recente publicada — ou é mais nova que ela.</summary>
        UpToDate,

        /// <summary>Há release publicado mais novo que esta cópia.</summary>
        Outdated,

        /// <summary>Não deu para saber: sem rede, limite de requisição, sem release ainda.</summary>
        NotChecked
    }

    /// <summary>Resultado da verificação, com o que o log precisa registrar (doc 02 §9).</summary>
    public sealed class UpdateCheckResult
    {
        internal UpdateCheckResult(UpdateState state, string installedVersion, string latestVersion, string detail)
        {
            State = state;
            InstalledVersion = installedVersion;
            LatestVersion = latestVersion;
            Detail = detail;
        }

        public UpdateState State { get; }

        public string InstalledVersion { get; }

        /// <summary>Versão do release mais recente, ou <c>null</c> quando não deu para saber.</summary>
        public string LatestVersion { get; }

        /// <summary>
        /// Uma linha, pronta para o log. Fica aqui e não em quem chama porque é o único
        /// registro que sobra de uma verificação que não bloqueia nada — e porque assim ela
        /// tem teste.
        /// </summary>
        public string Detail { get; }
    }

    /// <summary>
    /// Verificação de versão na inicialização (doc 01 §4, doc 02 §8.3, ADR-014).
    ///
    /// A ferramenta consulta o release mais recente publicado e compara com a própria versão.
    /// Desatualizada → aviso **não bloqueante** na tela 1, com o link de download.
    ///
    /// **Falha nunca bloqueia, e é a parte que mais importa.** Máquina de cliente sem rede,
    /// com proxy, atrás de firewall que barra o GitHub, ou no limite de 60 requisições por
    /// hora por IP da API não autenticada — em todos esses casos o resultado é
    /// <see cref="UpdateState.NotChecked"/>, uma linha no log, e o diagnóstico segue. O doc 01
    /// §4 é explícito: funciona sem internet depois de baixado.
    ///
    /// A separação entre esta classe e <see cref="ReleaseFeed"/> é a mesma dos coletores: aqui
    /// mora a decisão, que é função pura e tem teste; lá mora a fonte, que só se exercita em
    /// campo.
    /// </summary>
    public static class UpdateCheck
    {
        /// <summary>Repositório público decidido no ADR-002.</summary>
        public const string RepositorySlug = "Epicora-Software-House/epicora-checkup";

        /// <summary>
        /// API de releases do GitHub. Chamada não autenticada: nenhum token viaja no binário
        /// (ADR-002 opção B é explicitamente o que não se faz), ao custo do limite de 60
        /// requisições por hora por IP de origem.
        /// </summary>
        public const string LatestReleaseUrl =
            "https://api.github.com/repos/" + RepositorySlug + "/releases/latest";

        /// <summary>
        /// URL estável de download (doc 02 §8.1). Nome de asset fixo entre releases, para
        /// resolver sempre no binário mais recente e permitir guiar o técnico com um link só.
        /// </summary>
        public const string DownloadUrl =
            "https://github.com/" + RepositorySlug + "/releases/latest/download/EpicoraCheckup.exe";

        /// <summary>Tempo limite curto, do doc 02 §8.3. Aviso de versão não vale espera.</summary>
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Nome da tag do release, extraído da resposta da API.
        ///
        /// Por expressão regular, e não por parser de JSON, porque o Core não referencia
        /// biblioteca de JSON de propósito — são POCOs e interfaces, e a forma serializada é
        /// responsabilidade de quem serializa. Um campo de texto de uma resposta conhecida não
        /// justifica inverter isso; a alternativa era subir a verificação de versão para um
        /// projeto que já tem Newtonsoft e não tem nada a ver com o assunto.
        /// </summary>
        public static string TagOf(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody)) return null;

            var match = Regex.Match(responseBody, "\"tag_name\"\\s*:\\s*\"(?<tag>[^\"]*)\"");

            if (!match.Success) return null;

            var tag = match.Groups["tag"].Value.Trim();

            return tag.Length == 0 ? null : tag;
        }

        /// <summary>
        /// Compara a versão instalada com o corpo da resposta da API.
        /// </summary>
        public static UpdateCheckResult Evaluate(string installedVersion, string responseBody)
        {
            var installed = Parse(installedVersion);

            if (installed == null)
            {
                return new UpdateCheckResult(UpdateState.NotChecked, installedVersion, null,
                    $"versão instalada \"{installedVersion}\" não é um número de versão — nada a comparar");
            }

            var tag = TagOf(responseBody);

            if (tag == null)
            {
                return new UpdateCheckResult(UpdateState.NotChecked, Text(installed), null,
                    "a resposta não trouxe tag_name — nenhum release identificado");
            }

            var latest = Parse(tag);

            if (latest == null)
            {
                // Aviso errado na frente do cliente custa mais que aviso ausente: um
                // "v1.0-beta" comparado a 1.0.0 diria desatualizada sem base para isso.
                return new UpdateCheckResult(UpdateState.NotChecked, Text(installed), null,
                    $"tag \"{tag}\" fora do padrão vN.N.N — não comparada");
            }

            if (latest > installed)
            {
                return new UpdateCheckResult(UpdateState.Outdated, Text(installed), Text(latest),
                    $"instalada {Text(installed)}, publicada {Text(latest)} — DESATUALIZADA. {DownloadUrl}");
            }

            if (latest < installed)
            {
                // Build de bancada ou pré-release rodando antes de a tag existir. Não é aviso.
                return new UpdateCheckResult(UpdateState.UpToDate, Text(installed), Text(latest),
                    $"instalada {Text(installed)} é mais nova que a publicada {Text(latest)} — build não publicado");
            }

            return new UpdateCheckResult(UpdateState.UpToDate, Text(installed), Text(latest),
                $"instalada {Text(installed)} é a mais recente publicada");
        }

        /// <summary>
        /// Busca e compara, e **não propaga exceção nenhuma**.
        ///
        /// Engolir exceção é normalmente defeito; aqui é o requisito. Quem chama é a tela 1 na
        /// máquina de um cliente, e o custo de uma falha de rede virar erro na frente dele é
        /// desproporcional ao valor de um aviso de versão.
        /// </summary>
        public static UpdateCheckResult Run(string installedVersion, Func<string> fetch)
        {
            if (fetch == null) throw new ArgumentNullException(nameof(fetch));

            try
            {
                return Evaluate(installedVersion, fetch());
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult(UpdateState.NotChecked, installedVersion, null,
                    "não foi possível consultar o release mais recente — " + OneLine(ex.Message));
            }
        }

        /// <summary>
        /// Converte "v1.2.3" ou "1.2.3" em versão comparável, ou <c>null</c>.
        ///
        /// **Três componentes exatos, e nada além disso.** Recusar sufixo de pré-release
        /// (<c>v1.2.0-rc1</c>) e versão encurtada (<c>v1.2</c>) é deliberado: o
        /// <c>/releases/latest</c> do GitHub não devolve pré-release, o CI não publica tag
        /// fora de <c>vN.N.N</c>, e o schema exige N.N.N na versão da própria ferramenta.
        /// Fora desse formato há anomalia, e a resposta certa para anomalia nesta
        /// verificação é não dizer nada — não adivinhar uma comparação.
        /// </summary>
        private static Version Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var match = NumberPattern.Match(text.Trim());

            if (!match.Success) return null;

            Version parsed;

            return Version.TryParse(match.Groups["number"].Value, out parsed) ? parsed : null;
        }

        private static readonly Regex NumberPattern = new Regex(
            @"^v?(?<number>\d+\.\d+\.\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static string Text(Version version)
        {
            return version.ToString(3);
        }

        /// <summary>Mensagem de exceção em uma linha: o log tem uma linha por registro.</summary>
        private static string OneLine(string message)
        {
            if (string.IsNullOrEmpty(message)) return "(sem detalhe)";

            var single = message.Replace("\r", " ").Replace("\n", " ").Trim();

            return single.Length <= 200
                ? single
                : single.Substring(0, 200).Trim() + "…";
        }
    }
}
