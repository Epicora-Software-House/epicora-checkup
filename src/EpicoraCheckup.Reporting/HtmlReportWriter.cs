using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Reporting
{
    /// <summary>
    /// Relatório HTML autocontido (doc 02 §6).
    ///
    /// **Arquivo único: CSS embutido, sem CDN, sem JavaScript, sem fonte remota.** Precisa
    /// abrir em máquina sem internet — que é o caso de boa parte das visitas — e continuar
    /// legível daqui a cinco anos, quando nenhum CDN referenciado hoje estará no ar.
    ///
    /// A ordem das seções não é estética: score primeiro, riscos depois, e o bloco "não foi
    /// possível verificar" **separado**, nunca misturado aos riscos. Quem lê precisa
    /// distinguir "achamos isto" de "não conseguimos olhar isto" sem esforço.
    ///
    /// Impressão em A4 funciona: o técnico às vezes entrega em papel.
    /// </summary>
    public static class HtmlReportWriter
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string Write(JObject document, string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(path, Render(document), Utf8NoBom);
            return path;
        }

        public static string Render(JObject document)
        {
            var html = new StringBuilder();

            html.Append("<!DOCTYPE html>\n<html lang=\"pt-BR\">\n<head>\n");
            html.Append("<meta charset=\"utf-8\">\n");
            html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
            html.Append("<title>").Append(E(Title(document))).Append("</title>\n");
            html.Append("<style>\n").Append(Css).Append("\n</style>\n</head>\n<body>\n");

            RenderHeader(html, document);
            RenderScore(html, document);
            RenderFindings(html, document);
            RenderIndeterminate(html, document);
            RenderInventory(html, document);
            RenderFooter(html, document);

            html.Append("</body>\n</html>\n");
            return html.ToString();
        }

        // ------------------------------------------------------------ seções

        private static void RenderHeader(StringBuilder html, JObject document)
        {
            var manual = document["manual"] as JObject ?? new JObject();
            var client = document["client"] as JObject ?? new JObject();
            var execution = document["execution"] as JObject ?? new JObject();

            html.Append("<header>\n");
            html.Append("<h1>Diagnóstico de estação de trabalho</h1>\n");
            html.Append("<div class=\"ident\">\n");

            Field(html, "Cliente", (string)client["name"]);
            Field(html, "Unidade", (string)client["unit"]);
            Field(html, "Máquina", (string)manual["machineLabel"]);
            Field(html, "Responsável", (string)manual["responsible"]);
            Field(html, "Setor", (string)manual["department"]);
            Field(html, "Localização", (string)manual["physicalLocation"]);
            Field(html, "Patrimônio", (string)manual["assetTag"]);
            Field(html, "Diagnóstico", (string)execution["diagnosticId"]);
            Field(html, "Técnico", (string)execution["technician"]);
            Field(html, "Data", LocalDate((string)execution["startedAt"]));

            html.Append("</div>\n");

            var condition = (string)manual["physicalCondition"];
            var notes = (string)manual["notes"];

            if (!string.IsNullOrWhiteSpace(condition) || !string.IsNullOrWhiteSpace(notes))
            {
                html.Append("<div class=\"obs\">\n");
                if (!string.IsNullOrWhiteSpace(condition))
                    html.Append("<p><strong>Situação física observada:</strong> ").Append(E(condition)).Append("</p>\n");
                if (!string.IsNullOrWhiteSpace(notes))
                    html.Append("<p><strong>Observações:</strong> ").Append(E(notes)).Append("</p>\n");
                html.Append("</div>\n");
            }

            // Declaração de escopo. Está no relatório porque é o que o cliente pergunta, e a
            // resposta precisa estar escrita, não só ser verdadeira.
            html.Append("<p class=\"escopo\">Esta ferramenta lê apenas metadados de hardware, software e configuração. ")
                .Append("Não acessa conteúdo de arquivos, e-mails, mensagens ou histórico de navegação.</p>\n");

            var elevated = (bool?)execution["elevated"] ?? false;
            if (!elevated)
            {
                html.Append("<p class=\"aviso\">Esta coleta rodou <strong>sem privilégio de administrador</strong>. ")
                    .Append("Algumas verificações não puderam ser feitas e estão listadas no bloco ")
                    .Append("&ldquo;Não foi possível verificar&rdquo;.</p>\n");
            }

            html.Append("</header>\n");
        }

        private static void RenderScore(StringBuilder html, JObject document)
        {
            var score = document["score"] as JObject;
            if (score == null) return;

            var band = (string)score["band"] ?? "Red";
            var value = (int?)score["value"] ?? 0;

            html.Append("<section class=\"score banda-").Append(band.ToLowerInvariant()).Append("\">\n");
            html.Append("<div class=\"score-num\">").Append(value).Append("</div>\n");
            html.Append("<div class=\"score-txt\">\n");
            html.Append("<div class=\"score-rot\">Índice de saúde &mdash; ").Append(E(BandName(band))).Append("</div>\n");
            html.Append("<div class=\"veredito\">").Append(E(VerdictName((string)score["verdict"]))).Append("</div>\n");

            var drivers = score["verdictDrivenBy"] as JArray;
            if (drivers != null && drivers.Count > 0)
            {
                html.Append("<div class=\"drivers\">Determinado por: ")
                    .Append(E(string.Join(", ", drivers.Select(d => (string)d))))
                    .Append("</div>\n");
            }

            html.Append("</div>\n</section>\n");
        }

        private static void RenderFindings(StringBuilder html, JObject document)
        {
            var findings = Findings(document, "NonCompliant");

            html.Append("<section>\n<h2>Riscos e pontos de atenção</h2>\n");

            if (findings.Count == 0)
            {
                html.Append("<p class=\"vazio\">Nenhum risco identificado nesta máquina.</p>\n</section>\n");
                return;
            }

            foreach (var severity in new[] { "Critical", "High", "Medium", "Low", "Info" })
            {
                var doGrupo = findings.Where(f => (string)f["severity"] == severity).ToList();
                if (doGrupo.Count == 0) continue;

                html.Append("<h3 class=\"sev-").Append(severity.ToLowerInvariant()).Append("\">")
                    .Append(E(SeverityName(severity))).Append(" (").Append(doGrupo.Count).Append(")</h3>\n");

                foreach (var finding in doGrupo)
                    RenderFinding(html, finding, severity);
            }

            html.Append("</section>\n");
        }

        private static void RenderFinding(StringBuilder html, JObject finding, string severity)
        {
            var falsoPositivo = (bool?)finding["markedFalsePositive"] ?? false;

            html.Append("<article class=\"achado sev-").Append(severity.ToLowerInvariant());
            if (falsoPositivo) html.Append(" falso-positivo");
            html.Append("\">\n");

            html.Append("<h4>").Append(E((string)finding["title"])).Append("</h4>\n");

            var clientText = (string)finding["clientText"];
            if (!string.IsNullOrWhiteSpace(clientText))
                html.Append("<p>").Append(E(clientText)).Append("</p>\n");

            var action = (string)finding["recommendedAction"];
            if (!string.IsNullOrWhiteSpace(action))
                html.Append("<p class=\"acao\"><strong>Ação recomendada:</strong> ").Append(E(action)).Append("</p>\n");

            var evidence = finding["evidence"] as JObject;
            if (evidence != null && evidence.Count > 0)
            {
                html.Append("<table class=\"evidencia\">\n");
                foreach (var pair in evidence)
                    Row(html, ShortPath(pair.Key), Scalar(pair.Value));
                html.Append("</table>\n");
            }

            if (falsoPositivo)
            {
                html.Append("<p class=\"fp\"><strong>Marcado como falso positivo pelo técnico.</strong> ")
                    .Append(E((string)finding["falsePositiveJustification"])).Append("</p>\n");
            }

            html.Append("</article>\n");
        }

        private static void RenderIndeterminate(StringBuilder html, JObject document)
        {
            var findings = Findings(document, "Indeterminate");
            if (findings.Count == 0) return;

            html.Append("<section class=\"indeterminado\">\n<h2>Não foi possível verificar</h2>\n");
            html.Append("<p class=\"explicacao\">Os itens abaixo não pudemos avaliar. ")
                .Append("<strong>Não são problemas encontrados</strong> &mdash; são perguntas que ficaram sem resposta, ")
                .Append("e o motivo de cada uma está declarado.</p>\n");

            html.Append("<table class=\"lista\">\n");
            foreach (var finding in findings)
                Row(html, (string)finding["title"], (string)finding["indeterminateReason"] ?? "sem motivo registrado");
            html.Append("</table>\n</section>\n");
        }

        private static void RenderInventory(StringBuilder html, JObject document)
        {
            var collectors = document["collectors"] as JArray;
            if (collectors == null || collectors.Count == 0) return;

            html.Append("<section class=\"inventario\">\n<h2>Inventário detalhado</h2>\n");

            foreach (var token in collectors.OfType<JObject>())
            {
                var status = (string)token["status"] ?? "Failed";

                html.Append("<article class=\"coletor\">\n");
                html.Append("<h3>").Append(E((string)token["displayName"]))
                    .Append(" <span class=\"estado estado-").Append(status.ToLowerInvariant()).Append("\">")
                    .Append(E(StatusName(status))).Append("</span></h3>\n");

                var summary = (string)token["summary"];
                if (!string.IsNullOrWhiteSpace(summary))
                    html.Append("<p class=\"resumo\">").Append(E(summary)).Append("</p>\n");

                var skip = (string)token["skipReason"];
                if (!string.IsNullOrWhiteSpace(skip))
                    html.Append("<p class=\"motivo\">").Append(E(skip)).Append("</p>\n");

                var errors = token["errors"] as JArray;
                if (errors != null && errors.Count > 0)
                {
                    foreach (var error in errors.OfType<JObject>())
                        html.Append("<p class=\"motivo\">").Append(E((string)error["message"])).Append("</p>\n");
                }

                var data = token["data"] as JObject;
                if (data != null && data.Count > 0)
                {
                    html.Append("<table class=\"dados\">\n");
                    RenderObject(html, data, string.Empty);
                    html.Append("</table>\n");
                }

                html.Append("</article>\n");
            }

            html.Append("</section>\n");
        }

        /// <summary>
        /// Renderiza o payload genericamente, sem conhecer a forma de cada coletor.
        ///
        /// É deliberado: os dezesseis payloads têm formas diferentes e mudam com o campo.
        /// Um renderizador por coletor seria dezesseis lugares para esquecer de atualizar
        /// quando o schema evoluir; este mostra o que existir.
        /// </summary>
        private static void RenderObject(StringBuilder html, JObject node, string prefix)
        {
            foreach (var pair in node)
            {
                var label = string.IsNullOrEmpty(prefix) ? pair.Key : prefix + " › " + pair.Key;
                var value = pair.Value;

                var nested = value as JObject;
                if (nested != null) { RenderObject(html, nested, label); continue; }

                var array = value as JArray;
                if (array != null)
                {
                    if (array.Count == 0) { Row(html, label, "nenhum"); continue; }

                    for (var i = 0; i < array.Count; i++)
                    {
                        var item = array[i] as JObject;
                        if (item != null) RenderObject(html, item, $"{label} [{i + 1}]");
                        else Row(html, $"{label} [{i + 1}]", Scalar(array[i]));
                    }
                    continue;
                }

                Row(html, label, Scalar(value));
            }
        }

        private static void RenderFooter(StringBuilder html, JObject document)
        {
            var tool = document["tool"] as JObject ?? new JObject();
            var execution = document["execution"] as JObject ?? new JObject();

            html.Append("<footer>\n<p>");
            html.Append(E((string)tool["name"] ?? "EpicoraCheckup"))
                .Append(" versão ").Append(E((string)tool["version"] ?? "?"))
                .Append(" (").Append(E((string)tool["runtime"] ?? "?")).Append(")");

            var rules = (string)tool["rulesVersion"];
            if (!string.IsNullOrWhiteSpace(rules)) html.Append(" &middot; matriz ").Append(E(rules));

            html.Append(" &middot; schema ").Append(E((string)document["schemaVersion"] ?? "?"));
            html.Append("</p>\n<p>Coleta iniciada em ").Append(E(LocalDateTime((string)execution["startedAt"])))
                .Append(", concluída em ").Append(E(LocalDateTime((string)execution["finishedAt"])))
                .Append(" (").Append((int?)execution["durationSeconds"] ?? 0).Append(" s).</p>\n");
            html.Append("<p>Nenhum dado foi enviado para servidor algum.</p>\n</footer>\n");
        }

        // ------------------------------------------------------------ auxiliares

        private static IList<JObject> Findings(JObject document, string state)
        {
            var findings = document["findings"] as JArray;
            if (findings == null) return new List<JObject>();

            return findings.OfType<JObject>().Where(f => (string)f["state"] == state).ToList();
        }

        private static void Field(StringBuilder html, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            html.Append("<div><dt>").Append(E(label)).Append("</dt><dd>").Append(E(value)).Append("</dd></div>\n");
        }

        private static void Row(StringBuilder html, string label, string value)
        {
            html.Append("<tr><th>").Append(E(label)).Append("</th><td>").Append(E(value)).Append("</td></tr>\n");
        }

        private static string Scalar(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "não disponível";
            if (token.Type == JTokenType.Boolean) return (bool)token ? "sim" : "não";

            if (token.Type == JTokenType.Integer)
            {
                var number = (long)token;
                // Bytes são inteiros grandes; formatar aqui é responsabilidade da apresentação
                // (doc 02 §5), e é o que torna "500107862016" legível numa reunião.
                return number >= 1024L * 1024 * 1024
                    ? $"{number / 1024d / 1024 / 1024:0.#} GB ({number.ToString("N0", Cultura)})"
                    : number.ToString("N0", Cultura);
            }

            if (token.Type == JTokenType.Float) return ((double)token).ToString("0.##", Cultura);

            return token.ToString();
        }

        private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-BR");

        /// <summary>Último segmento do caminho pontilhado, que é o que significa algo na tela.</summary>
        private static string ShortPath(string path)
        {
            var parts = path.Split('.');
            return parts.Length == 0 ? path : parts[parts.Length - 1];
        }

        private static string Title(JObject document)
        {
            var manual = document["manual"] as JObject;
            var client = document["client"] as JObject;

            var machine = (string)manual?["machineLabel"] ?? "máquina";
            var name = (string)client?["name"] ?? "cliente";

            return $"Diagnóstico {machine} — {name}";
        }

        private static string LocalDate(string iso)
        {
            DateTimeOffset parsed;
            return DateTimeOffset.TryParse(iso, out parsed) ? parsed.ToString("dd/MM/yyyy", Cultura) : iso;
        }

        private static string LocalDateTime(string iso)
        {
            DateTimeOffset parsed;
            return DateTimeOffset.TryParse(iso, out parsed) ? parsed.ToString("dd/MM/yyyy HH:mm:ss", Cultura) : iso ?? "?";
        }

        private static string BandName(string band)
        {
            switch (band)
            {
                case "Green": return "Verde";
                case "Yellow": return "Amarelo";
                default: return "Vermelho";
            }
        }

        private static string VerdictName(string verdict)
        {
            switch (verdict)
            {
                case "Keep": return "Manter";
                case "Upgrade": return "Fazer upgrade";
                default: return "Substituir";
            }
        }

        private static string SeverityName(string severity)
        {
            switch (severity)
            {
                case "Critical": return "Crítico";
                case "High": return "Alto";
                case "Medium": return "Médio";
                case "Low": return "Baixo";
                default: return "Informativo";
            }
        }

        private static string StatusName(string status)
        {
            switch (status)
            {
                case "Completed": return "concluído";
                case "Skipped": return "ignorado";
                default: return "falhou";
            }
        }

        /// <summary>
        /// Escapa para HTML. Obrigatório: nome de máquina, observação do técnico e mensagem
        /// de erro vêm de digitação livre e de mensagem do sistema, e um &lt; solto quebraria
        /// o documento inteiro a partir dali.
        /// </summary>
        private static string E(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }

        // ------------------------------------------------------------ estilo

        private const string Css = @"
:root { --texto:#202020; --secundario:#696969; --borda:#dedede; --fundo:#fafafa; }
* { box-sizing: border-box; }
body { margin:0; padding:32px; font-family:'Segoe UI',system-ui,-apple-system,Arial,sans-serif;
       font-size:14px; line-height:1.55; color:var(--texto); background:#fff; max-width:1000px; }
h1 { font-size:24px; font-weight:600; margin:0 0 18px; }
h2 { font-size:18px; font-weight:600; margin:34px 0 14px; padding-bottom:6px; border-bottom:2px solid var(--borda); }
h3 { font-size:15px; font-weight:600; margin:22px 0 10px; }
h4 { font-size:14px; font-weight:600; margin:0 0 6px; }
p { margin:0 0 8px; }

header .ident { display:flex; flex-wrap:wrap; gap:4px 32px; margin-bottom:14px; }
header .ident div { min-width:190px; }
header dt { font-size:11px; text-transform:uppercase; letter-spacing:.4px; color:var(--secundario); }
header dd { margin:0 0 6px; font-weight:600; }
.obs { background:var(--fundo); border-left:3px solid var(--borda); padding:10px 14px; margin:12px 0; }
.escopo { font-size:12px; color:var(--secundario); border-top:1px solid var(--borda); padding-top:10px; margin-top:14px; }
.aviso { background:#fdf6e3; border-left:3px solid #9e7608; padding:10px 14px; font-size:13px; }

.score { display:flex; align-items:center; gap:22px; padding:18px 22px; margin:22px 0;
         border:1px solid var(--borda); border-left-width:6px; background:var(--fundo); }
.score-num { font-size:52px; font-weight:700; line-height:1; }
.score-rot { font-size:12px; text-transform:uppercase; letter-spacing:.5px; color:var(--secundario); }
.veredito { font-size:22px; font-weight:600; }
.drivers { font-size:12px; color:var(--secundario); }
.banda-green { border-left-color:#1c7a3e; } .banda-green .score-num { color:#1c7a3e; }
.banda-yellow { border-left-color:#9e7608; } .banda-yellow .score-num { color:#9e7608; }
.banda-red { border-left-color:#a81c1c; } .banda-red .score-num { color:#a81c1c; }

.achado { border:1px solid var(--borda); border-left-width:4px; padding:12px 16px; margin:0 0 10px; }
.sev-critical { border-left-color:#a81c1c; } h3.sev-critical { color:#a81c1c; }
.sev-high { border-left-color:#c44a16; } h3.sev-high { color:#c44a16; }
.sev-medium { border-left-color:#9e7608; } h3.sev-medium { color:#9e7608; }
.sev-low { border-left-color:#5c5c5c; } h3.sev-low { color:#5c5c5c; }
.sev-info { border-left-color:#787878; } h3.sev-info { color:#787878; }
.acao { font-size:13px; color:var(--secundario); }
.falso-positivo { opacity:.6; border-left-color:#606a80 !important; }
.fp { font-size:12px; color:var(--secundario); }

.indeterminado { background:var(--fundo); padding:14px 18px; border:1px solid var(--borda); }
.indeterminado h2 { margin-top:0; border-bottom-color:#606a80; }
.explicacao { font-size:13px; color:var(--secundario); }

table { width:100%; border-collapse:collapse; margin:8px 0; font-size:13px; }
th, td { text-align:left; vertical-align:top; padding:4px 10px 4px 0; border-bottom:1px solid #f0f0f0; }
th { font-weight:500; color:var(--secundario); width:38%; }
.evidencia { font-size:12px; margin-top:8px; }

.coletor { margin:0 0 16px; }
.estado { font-size:11px; font-weight:500; text-transform:uppercase; letter-spacing:.4px; padding:2px 7px; border-radius:2px; }
.estado-completed { background:#e6f2ea; color:#1c7a3e; }
.estado-skipped { background:#eceef2; color:#606a80; }
.estado-failed { background:#fbeaea; color:#a81c1c; }
.resumo { font-size:13px; }
.motivo { font-size:12px; color:var(--secundario); }
.vazio { color:#1c7a3e; font-weight:600; }

footer { margin-top:40px; padding-top:12px; border-top:1px solid var(--borda);
         font-size:11px; color:var(--secundario); }

@media print {
  @page { size:A4; margin:14mm; }
  body { padding:0; max-width:none; font-size:11px; }
  /* Cartão de achado partido entre páginas é o que torna um relatório impresso
     confuso: o título fica numa folha e a ação recomendada na seguinte. */
  .achado, .coletor, .score, section { break-inside:avoid; page-break-inside:avoid; }
  h2 { break-after:avoid; page-break-after:avoid; }
  .score-num { font-size:40px; }
}
";
    }
}
