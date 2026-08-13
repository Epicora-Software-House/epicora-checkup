using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EpicoraCheckup.Rules;

namespace EpicoraCheckup.App
{
    /// <summary>De onde a matriz de regras foi carregada, e as regras em si.</summary>
    internal sealed class LoadedMatrix
    {
        internal LoadedMatrix(IReadOnlyList<Rule> rules, string origin, string version)
        {
            Rules = rules;
            Origin = origin;
            Version = version;
        }

        internal IReadOnlyList<Rule> Rules { get; }

        /// <summary>
        /// Descrição da origem, para o log. Não é enfeite: relatório contestado meses depois
        /// exige saber QUAL matriz produziu aquele número, e com sobreposição por pasta a
        /// resposta deixa de ser "a que veio no executável".
        /// </summary>
        internal string Origin { get; }

        /// <summary>
        /// Versão da matriz carregada, para <c>tool.rulesVersion</c> (ADR-015). Vai para o
        /// arquivo de saída, e é o que amarra um número contestado a um conteúdo de matriz.
        /// </summary>
        internal string Version { get; }
    }

    /// <summary>
    /// Carrega a matriz de regras, embutida ou de pasta.
    ///
    /// **ADR-013.** O doc 01 §4 exige executável único e o doc 02 §3.5 exige que mudar uma
    /// regra não obrigue a recompilar. As duas coisas coexistem assim: a matriz viaja
    /// embutida como recurso, e uma pasta <c>rules/</c> AO LADO do executável tem
    /// precedência quando existe.
    ///
    /// A busca não sobe mais a árvore de diretórios. Subir servia ao desenvolvimento — o
    /// binário em bin/Release e a matriz na raiz do repositório —, e deixou de ser
    /// necessário: o recurso embutido é compilado a partir da mesma pasta, então em
    /// desenvolvimento ele já está atualizado. Em máquina de cliente, subir a árvore era
    /// risco: uma pasta <c>rules/</c> esquecida num nível acima passaria a valer sem
    /// ninguém pedir.
    /// </summary>
    internal static class RulesLocator
    {
        private const string ResourcePrefix = "rules/";

        internal static LoadedMatrix Load()
        {
            var directory = ExternalDirectory();

            if (directory != null)
            {
                // Lê o disco uma vez e deriva as duas coisas do MESMO conteúdo: reler para
                // calcular a versão abriria janela para o arquivo ter mudado no meio.
                var files = RuleRepository.ReadDirectory(directory);

                return new LoadedMatrix(
                    RuleRepository.LoadFromFiles(files), directory, RuleRepository.VersionOf(files));
            }

            var embedded = EmbeddedFiles();

            if (embedded.Count == 0)
            {
                throw new InvalidOperationException(
                    "a matriz de regras não foi embutida neste executável e não há pasta rules/ ao lado dele. " +
                    "Este binário está quebrado — não use o relatório que ele produzir.");
            }

            return new LoadedMatrix(
                RuleRepository.LoadFromFiles(embedded),
                "matriz embutida no executável",
                RuleRepository.VersionOf(embedded));
        }

        /// <summary>
        /// Pasta <c>rules/</c> ao lado do executável, ou <c>null</c>.
        ///
        /// Confere que é a pasta certa, e não uma homônima: a matriz sempre tem
        /// <c>storage.json</c>, que é a categoria mais importante do produto.
        /// </summary>
        private static string ExternalDirectory()
        {
            try
            {
                var exe = Assembly.GetExecutingAssembly().Location;
                var baseDirectory = Path.GetDirectoryName(exe);

                if (string.IsNullOrEmpty(baseDirectory)) return null;

                var candidate = Path.Combine(baseDirectory, "rules");

                return File.Exists(Path.Combine(candidate, "storage.json")) ? candidate : null;
            }
            catch (Exception)
            {
                // Caminho inacessível não é motivo para não rodar: cai na matriz embutida.
                return null;
            }
        }

        private static IList<KeyValuePair<string, string>> EmbeddedFiles()
        {
            var assembly = Assembly.GetExecutingAssembly();

            return assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                .Select(name => new KeyValuePair<string, string>(
                    name.Substring(ResourcePrefix.Length), ReadResource(assembly, name)))
                .ToList();
        }

        private static string ReadResource(Assembly assembly, string name)
        {
            using (var stream = assembly.GetManifestResourceStream(name))
            {
                if (stream == null) return string.Empty;

                // Sem BOM: os arquivos da matriz são gravados sem ele, mas quem editar num
                // Windows pode reintroduzi-lo, e o parser de JSON rejeita.
                using (var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false), true))
                {
                    var text = reader.ReadToEnd();
                    return text.Length > 0 && text[0] == '﻿' ? text.Substring(1) : text;
                }
            }
        }
    }
}
