using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Rules
{
    /// <summary>
    /// Carrega a matriz de regras de uma pasta rules/.
    ///
    /// A ORDEM DE CARGA É PARTE DO CONTRATO. Os arquivos são lidos em ordem ordinal de
    /// nome e as regras concatenadas nessa ordem, porque Score.VerdictDrivenBy preserva
    /// a ordem de carga — não a ordem de exibição, que é ordenada por severidade depois.
    /// Trocar a ordenação aqui muda o JSON de saída e quebra os golden files.
    /// </summary>
    public sealed class RuleRepository
    {
        /// <summary>Arquivo que declara a versão da matriz (ADR-015). Não contém regras.</summary>
        public const string VersionFileName = "matriz.json";

        /// <summary>
        /// Arquivos de apoio, que não contêm regras. Não são matriz: são tabelas
        /// consumidas pelos coletores (IDs de evento, builds do Windows, CPUs do Win11),
        /// a lista de exclusões da Fase 5 e a declaração de versão da matriz.
        /// </summary>
        private static readonly HashSet<string> SupportFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "startup-exclusions.json",
            "event-ids.json",
            "windows-builds.json",
            "win11-cpu-support.json",
            VersionFileName
        };

        public static IReadOnlyList<Rule> LoadFromDirectory(string rulesDirectory)
        {
            return LoadFromFiles(ReadDirectory(rulesDirectory));
        }

        /// <summary>
        /// Lê o conteúdo dos arquivos de uma pasta <c>rules/</c>, sem interpretá-los.
        ///
        /// Existe para que quem precisa da matriz E da versão dela leia o disco UMA vez: a
        /// versão é a impressão digital do conteúdo carregado (ADR-015), e conteúdo lido de
        /// novo pode não ser o mesmo — o arquivo pode ter mudado entre as duas leituras.
        /// </summary>
        public static IList<KeyValuePair<string, string>> ReadDirectory(string rulesDirectory)
        {
            if (!Directory.Exists(rulesDirectory))
                throw new DirectoryNotFoundException($"pasta de regras não encontrada: {rulesDirectory}");

            return Directory.GetFiles(rulesDirectory, "*.json")
                .Select(path => new KeyValuePair<string, string>(
                    Path.GetFileName(path), ReadWithoutBom(path)))
                .ToList();
        }

        /// <summary>
        /// Carrega a matriz a partir do CONTEÚDO dos arquivos, e não de uma pasta.
        ///
        /// Existe porque a matriz também viaja embutida no executável (ADR-013): arquivo
        /// único não tem pasta ao lado de onde ler. A ordenação e o filtro de arquivos de
        /// apoio vivem AQUI, e não em quem lê o disco, para que as duas origens produzam a
        /// mesma matriz — inclusive a ordem de carga, que é parte do contrato de saída.
        /// </summary>
        public static IReadOnlyList<Rule> LoadFromFiles(IEnumerable<KeyValuePair<string, string>> files)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));

            var ordered = MatrixFiles(files);

            var rules = new List<Rule>();
            foreach (var file in ordered)
                rules.AddRange(LoadFile(file.Key, file.Value));

            var duplicates = rules.GroupBy(r => r.Id, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicates.Count > 0)
                throw new InvalidDataException($"id de regra duplicado na matriz: {string.Join(", ", duplicates)}");

            return rules;
        }

        // ------------------------------------------------------------ versão da matriz

        /// <summary>
        /// Versão da matriz, para gravar em <c>tool.rulesVersion</c> (ADR-015).
        ///
        /// Duas partes, e a segunda é o que dá valor à primeira:
        /// <c>2026.08.12+9f3c1ab2</c>. A data vem declarada em <c>matriz.json</c> e é rótulo
        /// escolhido por quem revisou; a impressão digital é do conteúdo dos arquivos de regra
        /// efetivamente carregados, e não depende de ninguém lembrar de nada.
        ///
        /// A distinção importa porque a matriz pode vir de uma pasta <c>rules/</c> ao lado do
        /// executável (ADR-013). Nesse caso a data declarada é a de quem montou aquela pasta,
        /// e só a impressão digital responde "foi esta matriz que produziu este número".
        ///
        /// Devolve <c>null</c> se não houver nem declaração nem como calcular impressão —
        /// campo ausente é <c>null</c>, e o schema aceita.
        /// </summary>
        public static string VersionOf(IEnumerable<KeyValuePair<string, string>> files)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));

            var all = files.ToList();
            var declared = DeclaredVersion(all);
            var fingerprint = Fingerprint(MatrixFiles(all));

            if (fingerprint == null) return declared;

            return declared == null ? fingerprint : declared + "+" + fingerprint;
        }

        /// <summary>
        /// Data declarada em <c>matriz.json</c>, ou <c>null</c>.
        ///
        /// Ausência não é erro: matriz montada à mão numa pasta ao lado do executável não
        /// precisa declarar nada, e a impressão digital sozinha ainda identifica o conteúdo.
        /// </summary>
        private static string DeclaredVersion(IEnumerable<KeyValuePair<string, string>> files)
        {
            var file = files.FirstOrDefault(f => string.Equals(f.Key, VersionFileName, StringComparison.OrdinalIgnoreCase));

            if (file.Key == null || string.IsNullOrWhiteSpace(file.Value)) return null;

            try
            {
                var declared = (string)JObject.Parse(file.Value)["version"];
                return string.IsNullOrWhiteSpace(declared) ? null : declared.Trim();
            }
            catch (Exception)
            {
                // Declaração ilegível não pode impedir a avaliação: a matriz em si carregou.
                return null;
            }
        }

        /// <summary>
        /// Impressão digital do conteúdo, em 8 dígitos hexadecimais.
        ///
        /// Sobre os arquivos de REGRA, na ordem de carga, e não sobre a pasta inteira: as
        /// tabelas de apoio alimentam coletor, não matriz, e mexer numa delas não muda o
        /// critério de avaliação.
        ///
        /// O conteúdo é normalizado para <c>\n</c> antes de entrar no hash. Sem isso, o mesmo
        /// arquivo salvo por um editor de Windows produziria versão diferente sem nenhuma
        /// regra ter mudado — e a primeira vez que isso acontecesse ninguém confiaria mais no
        /// campo.
        /// </summary>
        private static string Fingerprint(IEnumerable<KeyValuePair<string, string>> orderedFiles)
        {
            var material = new StringBuilder();

            foreach (var file in orderedFiles)
            {
                // O nome entra junto: mover uma regra de arquivo muda a ordem de carga, e a
                // ordem de carga é parte da saída (Score.VerdictDrivenBy).
                material.Append(file.Key).Append('\n').Append(Normalize(file.Value)).Append('\n');
            }

            try
            {
                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(new UTF8Encoding(false).GetBytes(material.ToString()));
                    var hex = new StringBuilder(8);

                    // Quatro bytes bastam: isto identifica uma revisão de matriz para auditoria,
                    // não protege contra ninguém forjando colisão.
                    for (var i = 0; i < 4; i++)
                        hex.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));

                    return hex.ToString();
                }
            }
            catch (Exception)
            {
                // Política FIPS da máquina pode barrar a implementação de SHA-256. Perder a
                // impressão digital é ruim; não gerar o relatório por causa dela é pior.
                return null;
            }
        }

        private static string Normalize(string content)
        {
            if (string.IsNullOrEmpty(content)) return string.Empty;

            var text = content[0] == '\uFEFF' ? content.Substring(1) : content;
            return text.Replace("\r\n", "\n");
        }

        // ------------------------------------------------------------ carga

        /// <summary>
        /// Os arquivos que são matriz, na ordem de carga — ordinal por nome.
        ///
        /// Um só lugar, porque as duas coisas que dependem dela têm de concordar: a ordem em
        /// que as regras entram (que o <c>Score.VerdictDrivenBy</c> preserva) e a ordem em que
        /// o conteúdo entra na impressão digital.
        /// </summary>
        private static List<KeyValuePair<string, string>> MatrixFiles(IEnumerable<KeyValuePair<string, string>> files)
        {
            return files
                .Where(file => !SupportFiles.Contains(file.Key))
                .OrderBy(file => file.Key, StringComparer.Ordinal)
                .ToList();
        }

        private static IEnumerable<Rule> LoadFile(string name, string content)
        {
            var root = JObject.Parse(content);
            var array = root["rules"] as JArray;

            // Arquivo de categoria sem "rules" é erro de matriz, não ausência benigna:
            // uma categoria que silenciosamente não carrega some do relatório inteiro.
            if (array == null)
                throw new InvalidDataException($"{name}: não tem a lista \"rules\"");

            return array.Select(token => token.ToObject<Rule>()).ToList();
        }

        /// <summary>
        /// JSON gravado por PowerShell em máquina Windows pode vir com BOM UTF-8, e
        /// parser de JSON rejeita BOM. O mesmo cuidado existe no motor de referência.
        /// </summary>
        private static string ReadWithoutBom(string path)
        {
            var text = File.ReadAllText(path);
            return text.Length > 0 && text[0] == '\uFEFF' ? text.Substring(1) : text;
        }
    }
}
