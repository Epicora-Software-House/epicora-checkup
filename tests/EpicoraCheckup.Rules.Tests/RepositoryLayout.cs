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
        private static readonly Lazy<string> RootLazy = new Lazy<string>(() => FindRoot());

        internal static string Root => RootLazy.Value;

        internal static string RulesDirectory => Path.Combine(Root, "rules");

        internal static string FixturesDirectory => Path.Combine(Root, "tests", "fixtures");

        internal static string ExpectedDirectory => Path.Combine(Root, "tests", "expected");

        /// <summary>
        /// Sobe a partir da pasta de saída até achar a raiz.
        ///
        /// O caminho deste arquivo entra como segunda tentativa para o caso de a saída do
        /// build ficar fora da árvore do repositório — o que acontece quando estes mesmos
        /// fontes são compilados num andaime de fora, por exemplo para rodar os testes num
        /// Mac, já que net472 não executa aqui.
        /// </summary>
        private static string FindRoot(
            [System.Runtime.CompilerServices.CallerFilePath] string arquivoDesteFonte = null)
        {
            var root = ClimbFrom(AppDomain.CurrentDomain.BaseDirectory)
                       ?? ClimbFrom(Path.GetDirectoryName(arquivoDesteFonte));

            if (root != null) return root;

            throw new DirectoryNotFoundException(
                $"não achei a raiz do repositório subindo de {AppDomain.CurrentDomain.BaseDirectory} " +
                "— esperava encontrar uma pasta com rules/ e schema/");
        }

        private static string ClimbFrom(string start)
        {
            if (string.IsNullOrEmpty(start)) return null;

            var directory = new DirectoryInfo(start);

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

            return null;
        }
    }
}
