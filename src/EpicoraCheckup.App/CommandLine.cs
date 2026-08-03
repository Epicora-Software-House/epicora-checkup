using System;
using System.Collections.Generic;

namespace EpicoraCheckup.App
{
    /// <summary>
    /// Argumentos de linha de comando.
    ///
    /// O técnico em campo não passa nenhum: abre o executável e usa as telas. Os argumentos
    /// existem para desenvolvimento e para revisão de texto pelo comercial.
    /// </summary>
    internal sealed class CommandLine
    {
        internal bool ShowHelp { get; private set; }

        /// <summary>Caminho da fixture de demonstração, ou null para execução real.</summary>
        internal string DemoFixture { get; private set; }

        internal string Error { get; private set; }

        internal bool IsDemo => DemoFixture != null;

        internal static CommandLine Parse(IReadOnlyList<string> args)
        {
            var parsed = new CommandLine();

            for (var i = 0; i < args.Count; i++)
            {
                var arg = args[i];

                switch (arg)
                {
                    case "--help":
                    case "-h":
                    case "/?":
                        parsed.ShowHelp = true;
                        break;

                    case "--demonstracao":
                    case "--demo":
                        if (i + 1 >= args.Count)
                        {
                            parsed.Error = $"{arg} exige o caminho de um arquivo JSON de coleta.";
                            return parsed;
                        }
                        parsed.DemoFixture = args[++i];
                        break;

                    default:
                        parsed.Error = $"argumento desconhecido: {arg}";
                        return parsed;
                }
            }

            return parsed;
        }

        internal static string HelpText =>
            Strings.AppName + Environment.NewLine +
            Environment.NewLine +
            "  EpicoraCheckup.exe" + Environment.NewLine +
            "      Executa o diagnóstico nesta máquina." + Environment.NewLine +
            Environment.NewLine +
            "  EpicoraCheckup.exe --demonstracao <arquivo.json>" + Environment.NewLine +
            "      Percorre as telas com dados de um JSON de coleta já existente." + Environment.NewLine +
            "      Não coleta nada desta máquina e NÃO grava nenhum arquivo." + Environment.NewLine +
            "      Serve para revisar telas e textos de relatório." + Environment.NewLine +
            Environment.NewLine +
            "  EpicoraCheckup.exe --help" + Environment.NewLine +
            "      Mostra esta ajuda.";
    }
}
