using System;
using System.IO;

namespace EpicoraCheckup.Rules.Tests
{
    /// <summary>
    /// Localiza as pastas do repositório a partir do assembly de teste.
    ///
    /// Os testes leem a matriz e as fixtures REAIS do repositório, não copias embutidas
    /// como recurso. É deliberado: quem edita uma regra e esquece de regenerar os golden
    /// files precisa ver o teste ficar vermelho, e isso só acontece se o teste ler o
    /// mesmo arquivo que o motor de produção vai ler.
    /// </summary>
    internal static class RepositoryLayout
    {
        private static readonly Lazy<string> RootLazy = new Lazy<string>(FindRoot);

        internal static string Root => RootLazy.Value;

        internal static string RulesDirectory => Path.Combine(Root, "rules");

        internal static string FixturesDirectory => Path.Combine(Root, "tests", "fixtures");

        internal static string ExpectedDirectory => Path.Combine(Root, "tests", "expected");

        private static string FindRoot()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

            while (directory != null)
            {
                // Marcadores da raiz: as duas pastas que definem o contrato do projeto.
                if (Directory.Exists(Path.Combine(directory.FullName, "rules")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "schema")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                $"não achei a raiz do repositório subindo de {AppDomain.CurrentDomain.BaseDirectory} " +
                "— esperava encontrar uma pasta com rules/ e schema/");
        }
    }
}
