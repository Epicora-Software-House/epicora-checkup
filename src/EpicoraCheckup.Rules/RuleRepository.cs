using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        /// <summary>
        /// Arquivos de apoio, que não contêm regras. Não são matriz: são tabelas
        /// consumidas pelos coletores (IDs de evento, builds do Windows, CPUs do Win11)
        /// e a lista de exclusões da Fase 5.
        /// </summary>
        private static readonly HashSet<string> SupportFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "startup-exclusions.json",
            "event-ids.json",
            "windows-builds.json",
            "win11-cpu-support.json"
        };

        public static IReadOnlyList<Rule> LoadFromDirectory(string rulesDirectory)
        {
            if (!Directory.Exists(rulesDirectory))
                throw new DirectoryNotFoundException($"pasta de regras não encontrada: {rulesDirectory}");

            var files = Directory.GetFiles(rulesDirectory, "*.json")
                .Where(path => !SupportFiles.Contains(Path.GetFileName(path)))
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToList();

            var rules = new List<Rule>();
            foreach (var file in files)
                rules.AddRange(LoadFile(file));

            var duplicates = rules.GroupBy(r => r.Id, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicates.Count > 0)
                throw new InvalidDataException($"id de regra duplicado na matriz: {string.Join(", ", duplicates)}");

            return rules;
        }

        private static IEnumerable<Rule> LoadFile(string path)
        {
            var root = JObject.Parse(ReadWithoutBom(path));
            var array = root["rules"] as JArray;

            // Arquivo de categoria sem "rules" é erro de matriz, não ausência benigna:
            // uma categoria que silenciosamente não carrega some do relatório inteiro.
            if (array == null)
                throw new InvalidDataException($"{Path.GetFileName(path)}: não tem a lista \"rules\"");

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
