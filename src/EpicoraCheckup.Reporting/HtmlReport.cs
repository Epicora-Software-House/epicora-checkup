using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using EpicoraCheckup.Core.Contracts;
using EpicoraCheckup.Core.Model;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Reporting
{
    /// <summary>
    /// Relatório HTML individual — o que é entregue ao cliente por máquina.
    ///
    /// **Arquivo único e autocontido** (doc 02 §6): CSS embutido, sem CDN, sem JavaScript,
    /// sem fonte remota. Precisa abrir em máquina sem internet e continuar legível daqui a
    /// cinco anos. Impressão em A4 tem que funcionar, porque técnico em campo às vezes
    /// entrega em papel.
    ///
    /// Duas regras de conteúdo, e as duas vêm do princípio 6 do doc 01 — todo texto exibido é
    /// escrito para quem vai assinar a proposta:
    ///
    ///  1. **O que não foi possível verificar aparece com o motivo**, em bloco próprio, nunca
    ///     escondido e nunca misturado com problema encontrado.
    ///  2. **Achado marcado como falso positivo pelo técnico sai da lista de riscos** e vai
    ///     para uma seção própria com a justificativa. Ele continua no JSON — é assim que a
    ///     regra é corrigida —, mas apresentar ao cliente como risco algo que o próprio
    ///     técnico contesta é perder a conversa inteira.
    /// </summary>
    public static class HtmlReport
    {
        public static string Build(CheckupRun run, JObject document)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));

            var html = new StringBuilder();
            var dados = PayloadsPorColetor(run);

            html.Append("<!DOCTYPE html>\n<html lang=\"pt-BR\">\n<head>\n");
            html.Append("<meta charset=\"utf-8\">\n");
            html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
            html.Append("<title>").Append(E(Titulo(run))).Append("</title>\n");
            html.Append("<style>\n").Append(Css()).Append("</style>\n</head>\n<body>\n");

            Cabecalho(html, run, dados);
            Veredito(html, run);
            Riscos(html, run);
            NaoVerificado(html, run);
            FalsosPositivos(html, run);
            Etapas(html, run);
            Inventario(html, dados);
            Rodape(html, run);

            html.Append("</body>\n</html>\n");

            return html.ToString();
        }

        private static string Titulo(CheckupRun run)
        {
            var etiqueta = Texto(run.MachineLabel) ?? CheckupDocument.Hostname(run) ?? "máquina";

            return "Epicora Checkup — " + etiqueta;
        }

        // ------------------------------------------------------------ seções

        private static void Cabecalho(StringBuilder html, CheckupRun run, IDictionary<string, JObject> dados)
        {
            var maquina = Bloco(dados, "machine");

            html.Append("<header>\n<h1>Diagnóstico de máquina</h1>\n");
            html.Append("<p class=\"sub\">").Append(E(Texto(run.ClientName) ?? "cliente não informado"));

            var unidade = Texto(run.ClientUnit);
            if (unidade != null) html.Append(" · ").Append(E(unidade));

            html.Append("</p>\n</header>\n");

            html.Append("<section class=\"identificacao\">\n<table>\n");

            Linha(html, "Máquina", Texto(run.MachineLabel) ?? Campo(maquina, "hostname"));
            Linha(html, "Responsável", Texto(run.Responsible));
            Linha(html, "Setor", Texto(run.Department));
            Linha(html, "Localização", Texto(run.PhysicalLocation));
            Linha(html, "Patrimônio", Texto(run.AssetTag));
            Linha(html, "Equipamento", Concatena(Campo(maquina, "manufacturer"), Campo(maquina, "model")));
            Linha(html, "Número de série", Campo(maquina, "productSerial"));
            Linha(html, "Diagnóstico", Texto(run.DiagnosticId));
            Linha(html, "Técnico", Texto(run.Technician));
            Linha(html, "Data", run.FinishedAt.ToString("dd/MM/yyyy HH:mm", Cultura));

            html.Append("</table>\n</section>\n");

            var condicao = Texto(run.PhysicalCondition);
            var observacoes = Texto(run.Notes);

            if (condicao != null || observacoes != null)
            {
                html.Append("<section>\n<h2>Observações do técnico</h2>\n");
                if (condicao != null) html.Append("<p><strong>Situação física:</strong> ").Append(E(condicao)).Append("</p>\n");
                if (observacoes != null) html.Append("<p>").Append(E(observacoes)).Append("</p>\n");
                html.Append("</section>\n");
            }
        }

        private static void Veredito(StringBuilder html, CheckupRun run)
        {
            var score = run.Score;

            var faixa = score == null ? ScoreBand.Green : score.Band;
            var valor = score == null ? 100 : score.Value;

            html.Append("<section class=\"veredito faixa-").Append(faixa.ToString().ToLowerInvariant()).Append("\">\n");
            html.Append("<div class=\"indice\"><span class=\"numero\">").Append(valor.ToString(Cultura));
            html.Append("</span><span class=\"de\">/100</span></div>\n");
            html.Append("<div class=\"leitura\">\n<p class=\"rotulo\">Índice de saúde</p>\n");
            html.Append("<p class=\"veredito-texto\">").Append(E(NomeDoVeredito(score))).Append("</p>\n");

            var motivadores = score == null || score.VerdictDrivenBy == null
                ? new List<string>()
                : score.VerdictDrivenBy.ToList();

            if (motivadores.Count > 0)
            {
                // Dizer POR QUE a máquina é "Substituir" em vez de só afirmar que é — e
                // permitir ao cliente contestar a regra, não o número.
                html.Append("<p class=\"motivo\">Determinado por: ")
                    .Append(E(string.Join(", ", motivadores))).Append("</p>\n");
            }

            html.Append("</div>\n</section>\n");
        }

        private static void Riscos(StringBuilder html, CheckupRun run)
        {
            var riscos = run.Findings
                .Where(f => f.State == RuleState.NonCompliant && !f.MarkedFalsePositive)
                .OrderBy(f => f.Severity)
                .ToList();

            html.Append("<section>\n<h2>Riscos e pontos de atenção</h2>\n");

            if (riscos.Count == 0)
            {
                html.Append("<p class=\"vazio\">Nenhum risco identificado nesta máquina.</p>\n</section>\n");
                return;
            }

            foreach (var risco in riscos)
            {
                html.Append("<article class=\"achado sev-")
                    .Append(risco.Severity.ToString().ToLowerInvariant()).Append("\">\n");

                html.Append("<p class=\"severidade\">").Append(E(NomeDaSeveridade(risco.Severity))).Append("</p>\n");
                html.Append("<h3>").Append(E(risco.Title)).Append("</h3>\n");

                // clientText é o texto aprovado pelo comercial. Sem ele, o título é o que há —
                // e regra sem clientText não deveria estar habilitada em release.
                if (!string.IsNullOrWhiteSpace(risco.ClientText))
                    html.Append("<p>").Append(E(risco.ClientText)).Append("</p>\n");

                if (!string.IsNullOrWhiteSpace(risco.RecommendedAction))
                {
                    html.Append("<p class=\"acao\"><strong>Ação recomendada:</strong> ")
                        .Append(E(risco.RecommendedAction)).Append("</p>\n");
                }

                html.Append("</article>\n");
            }

            html.Append("</section>\n");
        }

        private static void NaoVerificado(StringBuilder html, CheckupRun run)
        {
            var indeterminados = run.Findings.Where(f => f.State == RuleState.Indeterminate).ToList();
            if (indeterminados.Count == 0) return;

            html.Append("<section class=\"indeterminado\">\n<h2>Não foi possível verificar</h2>\n");
            html.Append("<p class=\"explicacao\">Os itens abaixo não pudemos avaliar. ");
            html.Append("<strong>Não são problemas encontrados</strong> — são perguntas que ficaram sem resposta, ");
            html.Append("e o motivo de cada uma está declarado.</p>\n<ul>\n");

            foreach (var item in indeterminados)
            {
                html.Append("<li><strong>").Append(E(item.Title)).Append("</strong>");

                if (!string.IsNullOrWhiteSpace(item.IndeterminateReason))
                    html.Append(" — ").Append(E(item.IndeterminateReason));

                html.Append("</li>\n");
            }

            html.Append("</ul>\n</section>\n");
        }

        private static void FalsosPositivos(StringBuilder html, CheckupRun run)
        {
            var marcados = run.Findings.Where(f => f.MarkedFalsePositive).ToList();
            if (marcados.Count == 0) return;

            html.Append("<section class=\"falso-positivo\">\n<h2>Achados contestados pelo técnico</h2>\n");
            html.Append("<p class=\"explicacao\">O técnico marcou os itens abaixo como falso positivo durante a visita. ");
            html.Append("Eles não entram na lista de riscos, e a justificativa alimenta a correção da regra.</p>\n<ul>\n");

            foreach (var item in marcados)
            {
                html.Append("<li><strong>").Append(E(item.Title)).Append("</strong>");

                if (!string.IsNullOrWhiteSpace(item.FalsePositiveJustification))
                    html.Append(" — ").Append(E(item.FalsePositiveJustification));

                html.Append("</li>\n");
            }

            html.Append("</ul>\n</section>\n");
        }

        /// <summary>
        /// O que cada etapa da coleta respondeu. Está no relatório de propósito: é o que
        /// permite ao cliente ver que "não verificado" tem causa declarada, e não é omissão.
        /// </summary>
        private static void Etapas(StringBuilder html, CheckupRun run)
        {
            var problemas = run.Collectors.Where(c => c.Status != CollectorStatus.Completed).ToList();
            if (problemas.Count == 0) return;

            html.Append("<section>\n<h2>Etapas que não puderam ser concluídas</h2>\n<table>\n");

            foreach (var etapa in problemas)
            {
                var motivo = etapa.Status == CollectorStatus.Skipped
                    ? etapa.SkipReason
                    : etapa.TimedOut ? "tempo limite excedido" : PrimeiroErro(etapa);

                Linha(html, etapa.DisplayName ?? etapa.Id, motivo ?? "sem detalhe");
            }

            html.Append("</table>\n</section>\n");
        }

        private static void Inventario(StringBuilder html, IDictionary<string, JObject> dados)
        {
            html.Append("<section class=\"inventario\">\n<h2>Inventário</h2>\n");

            var maquina = Bloco(dados, "machine");
            var so = Bloco(dados, "os");
            var cpu = Bloco(dados, "cpu");
            var memoria = Bloco(dados, "memory");
            var disco = Sub(Bloco(dados, "storage"), "systemDisk");
            var volume = Sub(Bloco(dados, "storage"), "systemVolume");
            var rede = Bloco(dados, "network");
            var seguranca = Bloco(dados, "security");
            var antivirus = Bloco(dados, "antivirus");
            var software = Bloco(dados, "software");
            var inicializacao = Bloco(dados, "startup");
            var bateria = Bloco(dados, "battery");
            var win11 = Bloco(dados, "win11");

            html.Append("<table>\n");

            Linha(html, "Tipo", TipoDeMaquina(maquina));
            Linha(html, "Idade aproximada", Anos(Campo(maquina, "approxAgeYears")));
            Linha(html, "Sistema operacional", Concatena(Campo(so, "caption"), Campo(so, "displayVersion")));
            Linha(html, "Edição", Campo(so, "edition"));
            Linha(html, "Build", Campo(so, "buildNumber"));
            Linha(html, "Ativação", Ativacao(so));
            Linha(html, "Processador", Campo(cpu, "name"));
            Linha(html, "Núcleos", Campo(cpu, "physicalCores"));
            Linha(html, "Memória", Bytes(Campo(memoria, "totalBytes")));
            Linha(html, "Slots de memória", Slots(memoria));
            Linha(html, "Disco de sistema", DiscoDeSistema(disco));
            Linha(html, "Espaço livre", Porcentagem(Campo(volume, "freePercent")));
            Linha(html, "Saúde do disco", SaudeDoDisco(disco));
            Linha(html, "Rede", Conexao(rede));
            Linha(html, "BitLocker", Protecao(Sub(seguranca, "bitlocker"), "systemVolumeProtected"));
            Linha(html, "Firewall", Firewall(Sub(seguranca, "firewall")));
            Linha(html, "Área de trabalho remota", SimNao(Flag(Sub(seguranca, "rdp"), "enabled")));
            Linha(html, "SMBv1", SimNao(Flag(Sub(seguranca, "smb1"), "enabled")));
            Linha(html, "Antivírus", Antivirus(antivirus));
            Linha(html, "Programas instalados", Campo(software, "count"));
            Linha(html, "Programas na inicialização", Campo(inicializacao, "count"));
            Linha(html, "Bateria", Bateria(bateria));
            Linha(html, "Compatível com Windows 11", Windows11(win11));

            html.Append("</table>\n");

            Categorias(html, software);

            html.Append("</section>\n");
        }

        private static void Categorias(StringBuilder html, JObject software)
        {
            var classificacao = Sub(software, "classification");
            if (classificacao == null) return;

            var categorias = new[]
            {
                new[] { "remoteAccessTools", "Acesso remoto" },
                new[] { "edrAgents", "Agentes de EDR" },
                new[] { "antivirusProducts", "Antivírus de terceiro" },
                new[] { "backupAgents", "Backup" },
                new[] { "obsoleteRuntimes", "Componentes obsoletos" },
                new[] { "potentiallyUnwanted", "Programas indesejados em potencial" }
            };

            var linhas = new StringBuilder();

            foreach (var categoria in categorias)
            {
                var lista = classificacao[categoria[0]] as JArray;
                if (lista == null || lista.Count == 0) continue;

                Linha(linhas, categoria[1], string.Join(", ", lista.Select(item => (string)item)));
            }

            if (linhas.Length == 0) return;

            html.Append("<h3>Software de interesse</h3>\n<table>\n").Append(linhas).Append("</table>\n");
        }

        private static void Rodape(StringBuilder html, CheckupRun run)
        {
            html.Append("<footer>\n");
            html.Append("<p>Epicora Checkup ").Append(E(CheckupDocument.Version(run.ToolVersion)));
            html.Append(" · schema ").Append(CheckupDocument.SchemaVersion);
            html.Append(" · coleta de ").Append(run.DurationSeconds.ToString(Cultura)).Append(" s");
            html.Append(" · ").Append(run.FinishedAt.ToString("dd/MM/yyyy HH:mm:ss zzz", Cultura)).Append("</p>\n");

            html.Append("<p>Esta ferramenta lê apenas metadados de hardware, software e configuração. ");
            html.Append("Não acessa conteúdo de arquivos, e-mails, mensagens ou histórico de navegação. ");
            html.Append("Nada foi enviado para nenhum servidor.</p>\n");

            if (!run.Elevated)
            {
                html.Append("<p>A coleta rodou <strong>sem privilégio de administrador</strong>: ");
                html.Append("TPM, BitLocker e a verificação SMART do disco não puderam ser lidos.</p>\n");
            }

            html.Append("</footer>\n");
        }

        // ------------------------------------------------------------ formatação de valores

        private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-BR");

        private const string NaoVerificadoTexto = "não verificado";

        private static string TipoDeMaquina(JObject maquina)
        {
            var laptop = Flag(maquina, "isLaptop");

            if (laptop == true) return "Notebook";
            if (laptop == false) return "Desktop";

            return NaoVerificadoTexto;
        }

        private static string Ativacao(JObject so)
        {
            var status = Campo(Sub(so, "activation"), "status");

            switch (status)
            {
                case "Licensed": return "ativado";
                case "Unlicensed": return "NÃO ativado";
                case "OutOfTolerance":
                case "OutOfBox":
                case "Notification": return "ativação com pendência (" + status + ")";
                case "NonGenuine": return "licença não genuína";
                default: return NaoVerificadoTexto;
            }
        }

        private static string Slots(JObject memoria)
        {
            var usados = Campo(memoria, "usedSlots");
            var livres = Campo(memoria, "freeSlots");

            if (usados == null) return NaoVerificadoTexto;

            return livres == null
                ? usados + " ocupado(s), livres não verificados"
                : usados + " ocupado(s), " + livres + " livre(s)";
        }

        private static string DiscoDeSistema(JObject disco)
        {
            if (disco == null) return NaoVerificadoTexto;

            var tipo = Campo(disco, "mediaType");
            var tamanho = Bytes(Campo(disco, "sizeBytes"));

            var rotulo = tipo == "HDD" ? "HD mecânico" : tipo == "SSD" ? "SSD" : "tipo não identificado";

            return Concatena(rotulo, tamanho) ?? NaoVerificadoTexto;
        }

        private static string SaudeDoDisco(JObject disco)
        {
            var falha = Flag(disco, "failurePredicted");

            if (falha == true) return "FALHA PREVISTA pelo próprio disco";
            if (falha == false) return "sem falha prevista";

            return NaoVerificadoTexto;
        }

        private static string Conexao(JObject rede)
        {
            var tipo = Campo(rede, "primaryConnectionType");

            var rotulo = tipo == "Wired" ? "cabo" : tipo == "Wireless" ? "Wi-Fi" : null;
            var nome = Campo(rede, "primaryAdapterName");

            if (rotulo == null && nome == null) return NaoVerificadoTexto;

            var texto = Concatena(nome, rotulo == null ? null : "(" + rotulo + ")");

            return Flag(rede, "linkDowngraded") == true
                ? texto + " — negociando abaixo da capacidade da placa"
                : texto;
        }

        private static string Protecao(JObject bloco, string campo)
        {
            var valor = Flag(bloco, campo);

            if (valor == true) return "ativo";
            if (valor == false) return "não";

            return NaoVerificadoTexto;
        }

        private static string Firewall(JObject firewall)
        {
            var desativado = Flag(firewall, "anyProfileDisabled");

            if (desativado == true) return "desativado em ao menos um perfil";
            if (desativado == false) return "ativo em todos os perfis";

            return NaoVerificadoTexto;
        }

        private static string Antivirus(JObject antivirus)
        {
            var produtos = antivirus == null ? null : antivirus["products"] as JArray;

            var nomes = produtos == null
                ? new List<string>()
                : produtos.Select(p => (string)p["displayName"]).Where(n => n != null).ToList();

            var inventario = antivirus == null ? null : antivirus["securitySoftwareInInventory"] as JArray;

            if (nomes.Count > 0) return string.Join(", ", nomes);

            // Cruzamento com o inventário de software: é o que impede escrever "sem antivírus"
            // para quem tem EDR corporativo que a Central de Segurança não enxerga.
            if (inventario != null && inventario.Count > 0)
                return string.Join(", ", inventario.Select(item => (string)item)) + " (pelo inventário de software)";

            return NaoVerificadoTexto;
        }

        private static string Bateria(JObject bateria)
        {
            if (bateria == null) return "não se aplica";

            var desgaste = Campo(bateria, "wearPercent");
            if (desgaste == null) return "presente, desgaste " + NaoVerificadoTexto;

            var baterias = bateria["batteries"] as JArray;

            var ciclos = baterias != null && baterias.Count > 0 && baterias[0]["cycleCount"].Type != JTokenType.Null
                ? ", " + baterias[0]["cycleCount"] + " ciclos"
                : string.Empty;

            return "desgaste de " + desgaste + "%" + ciclos;
        }

        private static string Windows11(JObject win11)
        {
            if (win11 == null) return NaoVerificadoTexto;

            var elegivel = Flag(win11, "eligible");
            var bloqueios = win11["blockers"] as JArray;

            if (elegivel == true) return "sim";

            if (elegivel == false)
            {
                var motivos = bloqueios == null || bloqueios.Count == 0
                    ? string.Empty
                    : " (" + string.Join(", ", bloqueios.Select(b => (string)b)) + ")";

                return "não" + motivos;
            }

            // null NÃO é "não migra": é "faltou informação para responder".
            return "não foi possível concluir";
        }

        private static string Bytes(string valor)
        {
            long bytes;
            if (valor == null || !long.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out bytes))
                return null;

            return Math.Round(bytes / 1073741824d, 1).ToString("0.#", Cultura) + " GB";
        }

        private static string Porcentagem(string valor)
        {
            var numero = Numero(valor);

            return numero == null ? null : numero + "%";
        }

        private static string Anos(string valor)
        {
            var numero = Numero(valor);

            return numero == null ? null : "aproximadamente " + numero + " anos";
        }

        /// <summary>
        /// Número do JSON — sempre com ponto decimal — no formato de quem lê o relatório.
        /// "10.0% livre" num documento em português é ruído que o cliente nota.
        /// </summary>
        private static string Numero(string valor)
        {
            double numero;

            if (valor == null ||
                !double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out numero))
            {
                return null;
            }

            return numero.ToString("0.#", Cultura);
        }

        private static string SimNao(bool? valor)
        {
            if (valor == true) return "sim";
            if (valor == false) return "não";

            return NaoVerificadoTexto;
        }

        private static string NomeDoVeredito(Score score)
        {
            if (score == null) return "não avaliado";

            switch (score.Verdict)
            {
                case Verdict.Keep: return "Manter";
                case Verdict.Upgrade: return "Fazer upgrade";
                case Verdict.Replace: return "Substituir";
                default: return "não avaliado";
            }
        }

        private static string NomeDaSeveridade(Severity severity)
        {
            switch (severity)
            {
                case Severity.Critical: return "Crítico";
                case Severity.High: return "Alto";
                case Severity.Medium: return "Médio";
                case Severity.Low: return "Baixo";
                default: return "Informativo";
            }
        }

        // ------------------------------------------------------------ acesso ao payload

        private static IDictionary<string, JObject> PayloadsPorColetor(CheckupRun run)
        {
            var mapa = new Dictionary<string, JObject>(StringComparer.Ordinal);

            foreach (var result in run.Collectors)
            {
                if (result == null || result.Status != CollectorStatus.Completed || result.Id == null) continue;

                var data = result.Data as JObject;
                if (data != null) mapa[result.Id] = data;
            }

            return mapa;
        }

        private static JObject Bloco(IDictionary<string, JObject> dados, string id)
        {
            JObject bloco;
            return dados.TryGetValue(id, out bloco) ? bloco : null;
        }

        private static JObject Sub(JObject parent, string nome)
        {
            return parent == null ? null : parent[nome] as JObject;
        }

        /// <summary>
        /// Valor de campo como texto, ou <c>null</c> quando ausente ou nulo.
        ///
        /// Texto sai literal em vez de serializado: <c>ToString</c> num token de texto devolve
        /// o valor ENTRE ASPAS e com escape de JSON, e isso vazaria para a página do cliente.
        /// </summary>
        private static string Campo(JObject bloco, string nome)
        {
            if (bloco == null) return null;

            var token = bloco[nome];
            if (token == null || token.Type == JTokenType.Null) return null;

            return token.Type == JTokenType.String
                ? (string)token
                : token.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static bool? Flag(JObject bloco, string nome)
        {
            if (bloco == null) return null;

            var token = bloco[nome];

            return token == null || token.Type != JTokenType.Boolean ? (bool?)null : (bool)token;
        }

        private static string Texto(string valor)
        {
            return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
        }

        private static string Concatena(string a, string b)
        {
            if (a == null) return b;
            if (b == null) return a;

            return a + " " + b;
        }

        private static string PrimeiroErro(CollectorResult resultado)
        {
            return resultado.Errors == null || resultado.Errors.Count == 0 ? null : resultado.Errors[0].Message;
        }

        private static void Linha(StringBuilder html, string rotulo, string valor)
        {
            html.Append("<tr><th>").Append(E(rotulo)).Append("</th><td>");
            html.Append(E(valor ?? NaoVerificadoTexto)).Append("</td></tr>\n");
        }

        /// <summary>
        /// Escapa texto para HTML.
        ///
        /// Não é paranoia: nome de máquina, observação do técnico e nome de programa instalado
        /// entram no relatório e vêm de fora. Um <c>&lt;</c> no meio de um nome de programa
        /// quebraria a página que o cliente recebe.
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

        private static string Css()
        {
            // Sem fonte remota: a pilha abaixo existe em qualquer Windows e em qualquer Mac.
            return @"
:root { --texto:#1a1d21; --secundario:#5b6470; --borda:#dfe3e8; --fundo:#ffffff;
        --verde:#1e7a4b; --amarelo:#b07000; --vermelho:#b3261e; --azul:#12406b; }
* { box-sizing:border-box; }
body { margin:0 auto; padding:32px 28px 56px; max-width:900px; background:var(--fundo);
       color:var(--texto); font-family:'Segoe UI',-apple-system,Helvetica,Arial,sans-serif;
       font-size:15px; line-height:1.55; }
header { border-bottom:2px solid var(--azul); padding-bottom:12px; margin-bottom:24px; }
h1 { margin:0; font-size:26px; letter-spacing:-.01em; }
h2 { font-size:18px; margin:32px 0 12px; padding-bottom:6px; border-bottom:1px solid var(--borda); }
h3 { font-size:15px; margin:0 0 6px; }
p { margin:6px 0; }
.sub { color:var(--secundario); font-size:14px; margin:4px 0 0; }
table { width:100%; border-collapse:collapse; margin:8px 0 16px; }
th, td { text-align:left; vertical-align:top; padding:7px 10px; border-bottom:1px solid var(--borda);
         font-weight:400; font-size:14px; }
th { width:34%; color:var(--secundario); }
.veredito { display:flex; align-items:center; gap:22px; padding:18px 20px; border-radius:6px;
            border:1px solid var(--borda); border-left-width:8px; margin:24px 0; }
.veredito .numero { font-size:42px; font-weight:600; line-height:1; }
.veredito .de { font-size:16px; color:var(--secundario); }
.veredito .rotulo { color:var(--secundario); font-size:13px; text-transform:uppercase;
                    letter-spacing:.06em; margin:0; }
.veredito-texto { font-size:22px; font-weight:600; margin:2px 0 0; }
.motivo { color:var(--secundario); font-size:13px; }
.faixa-green { border-left-color:var(--verde); } .faixa-green .numero { color:var(--verde); }
.faixa-yellow { border-left-color:var(--amarelo); } .faixa-yellow .numero { color:var(--amarelo); }
.faixa-red { border-left-color:var(--vermelho); } .faixa-red .numero { color:var(--vermelho); }
.achado { border:1px solid var(--borda); border-left-width:6px; border-radius:5px;
          padding:12px 16px; margin:12px 0; }
.severidade { font-size:11px; text-transform:uppercase; letter-spacing:.08em;
              color:var(--secundario); margin:0 0 2px; font-weight:600; }
.sev-critical { border-left-color:var(--vermelho); } .sev-high { border-left-color:#d1512b; }
.sev-medium { border-left-color:var(--amarelo); } .sev-low { border-left-color:#8a8f98; }
.sev-info { border-left-color:var(--azul); }
.acao { font-size:14px; }
.indeterminado ul, .falso-positivo ul { padding-left:20px; margin:8px 0; }
.indeterminado li, .falso-positivo li { margin:4px 0; font-size:14px; }
.explicacao { color:var(--secundario); font-size:14px; }
.vazio { color:var(--verde); font-weight:600; }
footer { margin-top:40px; padding-top:14px; border-top:1px solid var(--borda);
         color:var(--secundario); font-size:12px; }
@media print {
  body { padding:0; max-width:none; font-size:11pt; }
  h2 { page-break-after:avoid; }
  .achado, section { page-break-inside:avoid; }
  .veredito { border:1px solid #000; }
}
";
        }
    }
}
