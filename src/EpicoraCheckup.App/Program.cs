using System;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Forms;

namespace EpicoraCheckup.App
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var cmd = CommandLine.Parse(args);

            if (cmd.Error != null)
            {
                Show(cmd.Error + Environment.NewLine + Environment.NewLine + CommandLine.HelpText, MessageBoxIcon.Warning);
                return 2;
            }

            if (cmd.ShowHelp)
            {
                Show(CommandLine.HelpText, MessageBoxIcon.Information);
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Exceção não tratada na thread da UI não pode fechar a janela em silêncio na
            // frente do cliente. Mostra, e segue se der.
            Application.ThreadException += (s, e) => ShowUnexpected(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowUnexpected(e.ExceptionObject as Exception);

            var session = new SessionState
            {
                IsElevated = IsElevated(),
                IsDemo = cmd.IsDemo,
                DemoFixturePath = cmd.DemoFixture,
                OutputDirectory = DefaultOutputDirectory(),
                StartedAt = DateTimeOffset.Now
            };

            session.LoadIdentification();

            using (var form = new MainForm(session))
            {
                Application.Run(form);
            }

            return 0;
        }

        /// <summary>
        /// Elevação é ESTADO, não pré-condição — ADR-011. O manifest pede highestAvailable,
        /// então a ferramenta abre nos dois casos e é aqui que se descobre em qual está.
        /// </summary>
        private static bool IsElevated()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception)
            {
                // Não conseguir determinar é diferente de não ter privilégio, mas para o
                // efeito prático é o caminho conservador: assume não elevado, e as fontes
                // privilegiadas resolvem Indeterminate em vez de falharem no meio.
                return false;
            }
        }

        /// <summary>
        /// Pasta de saída ao lado do executável. Zero instalação: nada em Program Files,
        /// nada em AppData, nada no registro (doc 01 §4).
        /// </summary>
        private static string DefaultOutputDirectory()
        {
            var exe = Assembly.GetExecutingAssembly().Location;
            var baseDir = Path.GetDirectoryName(exe) ?? Directory.GetCurrentDirectory();

            return Path.Combine(baseDir, "EpicoraCheckup");
        }

        private static void ShowUnexpected(Exception ex)
        {
            var message = ex == null ? "(sem detalhe)" : ex.Message;
            Show(string.Format(Strings.ErroInesperado, message), MessageBoxIcon.Error);
        }

        private static void Show(string text, MessageBoxIcon icon)
        {
            MessageBox.Show(text, Strings.AppName, MessageBoxButtons.OK, icon);
        }
    }
}
