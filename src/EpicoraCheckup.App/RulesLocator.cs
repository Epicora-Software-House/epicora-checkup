using System;
using System.IO;
using System.Reflection;

namespace EpicoraCheckup.App
{
    /// <summary>
    /// Acha a pasta <c>rules/</c> em tempo de execução.
    ///
    /// Duas situações, e as duas precisam funcionar:
    ///
    ///  - **Distribuído:** a pasta fica ao lado do executável.
    ///  - **Desenvolvimento:** o binário está em bin/Release/net472, e a matriz na raiz do
    ///    repositório. Sobe até achar.
    ///
    /// **Ponto aberto, registrado em src/README.md.** O doc 01 §4 exige executável único e o
    /// doc 02 §3.5 exige que mudar uma regra não obrigue a recompilar. Ler de pasta externa
    /// atende o segundo e contraria o primeiro. Embutir como recurso faz o inverso. Não é
    /// decisão para tomar dentro deste arquivo — por ora lê de pasta, que é o que permite o
    /// comercial revisar texto sem build.
    /// </summary>
    internal static class RulesLocator
    {
        internal static string Find()
        {
            var exe = Assembly.GetExecutingAssembly().Location;
            var start = Path.GetDirectoryName(exe) ?? Directory.GetCurrentDirectory();

            var directory = new DirectoryInfo(start);

            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "rules");

                // Confere que é a pasta certa, e não uma homônima: a matriz sempre tem
                // storage.json, que é a categoria mais importante do produto.
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "storage.json")))
                    return candidate;

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "não encontrei a pasta rules/ com a matriz de regras. " +
                $"Procurei a partir de {start} subindo até a raiz do volume. " +
                "A pasta precisa estar ao lado do executável.");
        }
    }
}
