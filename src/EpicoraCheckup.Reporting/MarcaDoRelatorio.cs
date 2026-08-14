using System;
using System.IO;
using System.Reflection;

namespace EpicoraCheckup.Reporting
{
    /// <summary>
    /// Os ativos de marca que o relatório HTML embute como data URI.
    ///
    /// Vive aqui, e não em <c>App</c>, porque Reporting não pode depender de WinForms nem
    /// do executável (doc 02 §2) — é a mesma regra que permite o consolidador da Fase 4
    /// gerar relatório sem abrir janela.
    ///
    /// Falha de recurso degrada para vazio e o relatório sai sem logotipo, com o texto
    /// intacto. Um diagnóstico que não é gravado porque o logotipo não abriu seria perder
    /// a visita inteira por causa da moldura.
    /// </summary>
    internal static class MarcaDoRelatorio
    {
        /// <summary>Logotipo roxo, já como data URI pronto para o atributo src.</summary>
        internal static readonly string LogotipoDataUri = DataUri("marca-html/logo-roxo.svg", "image/svg+xml");

        /// <summary>Alexandria SemiBold, data URI para o @font-face.</summary>
        internal static readonly string FonteDataUri = DataUri("marca-html/Alexandria-SemiBold.woff2", "font/woff2");

        /// <summary>Texto da SIL Open Font License 1.1, exigido junto de quem redistribui a fonte.</summary>
        internal static readonly string LicencaDaFonte = Texto("marca-html/OFL.txt");

        private static string DataUri(string recurso, string tipo)
        {
            var bytes = Ler(recurso);
            if (bytes == null) return string.Empty;

            // Base64 e não o SVG cru de propósito. O SVG traz xmlns="http://www.w3.org/..."
            // no cabeçalho — um identificador de namespace, não um endereço que o navegador
            // vá buscar, mas que faria a verificação de autocontenção do relatório apontar
            // uma dependência remota que não existe. Em base64 não há esse falso positivo,
            // e é a mesma forma que os decks comerciais usam.
            return "data:" + tipo + ";base64," + Convert.ToBase64String(bytes);
        }

        private static string Texto(string recurso)
        {
            var bytes = Ler(recurso);
            return bytes == null ? string.Empty : new StreamReader(new MemoryStream(bytes)).ReadToEnd();
        }

        private static byte[] Ler(string recurso)
        {
            try
            {
                using (var fluxo = Assembly.GetExecutingAssembly().GetManifestResourceStream(recurso))
                {
                    if (fluxo == null) return null;

                    using (var memoria = new MemoryStream())
                    {
                        fluxo.CopyTo(memoria);
                        return memoria.ToArray();
                    }
                }
            }
            catch (IOException)
            {
                return null;
            }
        }
    }
}
