using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Rules
{
    /// <summary>
    /// Marcador para "o caminho não existe no documento".
    ///
    /// Existe como tipo próprio, e não como null, porque ausente e nulo-explícito não
    /// são a mesma coisa para todos os operadores: <c>equals</c> contra null é
    /// verdadeiro para um campo nulo e falso para um campo ausente. Colapsar os dois
    /// mudaria o resultado da matriz.
    /// </summary>
    public sealed class Missing
    {
        public static readonly Missing Instance = new Missing();

        private Missing() { }

        public override string ToString() => "(ausente)";
    }

    /// <summary>Resultado de ler um caminho pontilhado do documento de coleta.</summary>
    public sealed class ReadResult
    {
        /// <summary>
        /// Preenchido quando o dado não pôde ser lido por causa do coletor — ausente da
        /// execução, ignorado ou falhado. É diferente de "o campo não existe": aqui
        /// existe um motivo em linguagem de relatório para mostrar ao cliente.
        /// </summary>
        public string UnavailableReason { get; private set; }

        public bool IsUnavailable => UnavailableReason != null;

        /// <summary><see cref="Missing"/>.Instance, ou um <see cref="JToken"/> que pode ser nulo.</summary>
        public object Value { get; private set; }

        public static ReadResult Unavailable(string reason) => new ReadResult { UnavailableReason = reason, Value = Missing.Instance };

        public static ReadResult NotFound() => new ReadResult { Value = Missing.Instance };

        public static ReadResult Found(JToken token) => new ReadResult { Value = token };

        /// <summary>Ausente, nulo explícito, ou indisponível por causa do coletor.</summary>
        public bool IsNullish => IsNullish_(Value);

        internal static bool IsNullish_(object v)
        {
            if (ReferenceEquals(v, Missing.Instance)) return true;
            if (v == null) return true;
            var token = v as JToken;
            return token != null && (token.Type == JTokenType.Null || token.Type == JTokenType.Undefined);
        }
    }

    /// <summary>
    /// Lê caminhos pontilhados de um documento de coleta.
    ///
    /// Um caminho que começa com "collectors" é resolvido pelo id do coletor, e o estado
    /// do coletor é verificado ANTES do caminho: coletor que não está Completed torna
    /// todo dado dele indisponível, com motivo. É o que garante que falha de coleta
    /// vire Indeterminate em vez de achado negativo.
    /// </summary>
    public sealed class DocumentReader
    {
        private readonly JObject _document;

        public DocumentReader(JObject document)
        {
            _document = document;
        }

        public ReadResult Read(string path)
        {
            var segments = new List<string>(path.Split('.'));
            JToken node;

            if (segments.Count > 0 && segments[0] == "collectors")
            {
                var wantedId = segments.Count > 1 ? segments[1] : null;
                var collector = FindCollector(wantedId);

                if (collector == null)
                    return ReadResult.Unavailable($"coletor \"{wantedId}\" ausente da execução");

                var status = (string)collector["status"];
                if (status != "Completed")
                {
                    var why = FirstNonNull(
                        (string)collector["skipReason"],
                        FirstErrorMessage(collector)) ?? "sem detalhe";
                    var label = status == "Skipped" ? "ignorado" : "falhou";
                    return ReadResult.Unavailable($"coletor \"{(string)collector["displayName"]}\" {label} — {why}");
                }

                node = collector["data"];
                // Remove "collectors", "<id>" e "data": o resto do caminho é relativo ao payload.
                segments.RemoveRange(0, System.Math.Min(3, segments.Count));
            }
            else
            {
                node = _document;
            }

            foreach (var segment in segments)
            {
                // Só objeto e array são navegáveis. Escalar no meio do caminho é ausência,
                // não erro — a regra que depende dele resolve Indeterminate.
                var container = node as JContainer;
                if (container == null) return ReadResult.NotFound();

                var next = SelectChild(container, segment);
                if (next == null) return ReadResult.NotFound();

                node = next;
            }

            return ReadResult.Found(node);
        }

        /// <summary>
        /// Devolve o filho, ou null quando a chave não existe. Distingue "chave existe
        /// com valor nulo" de "chave não existe": o primeiro devolve o token nulo.
        /// </summary>
        private static JToken SelectChild(JContainer container, string segment)
        {
            var obj = container as JObject;
            if (obj != null)
            {
                JToken value;
                return obj.TryGetValue(segment, out value) ? value : null;
            }

            var array = container as JArray;
            if (array != null)
            {
                int index;
                if (!int.TryParse(segment, out index)) return null;
                if (index < 0 || index >= array.Count) return null;
                return array[index];
            }

            return null;
        }

        private JObject FindCollector(string id)
        {
            var collectors = _document["collectors"] as JArray;
            if (collectors == null) return null;

            return collectors
                .OfType<JObject>()
                .FirstOrDefault(c => (string)c["id"] == id);
        }

        private static string FirstErrorMessage(JObject collector)
        {
            var errors = collector["errors"] as JArray;
            if (errors == null || errors.Count == 0) return null;

            var first = errors[0] as JObject;
            return first == null ? null : (string)first["message"];
        }

        private static string FirstNonNull(params string[] candidates)
        {
            foreach (var candidate in candidates)
                if (candidate != null) return candidate;

            return null;
        }
    }
}
