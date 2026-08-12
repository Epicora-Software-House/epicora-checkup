using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using EpicoraCheckup.App.Controls;
using EpicoraCheckup.Reporting;

namespace EpicoraCheckup.App.Screens
{
    /// <summary>
    /// Tela 7 — arquivos gerados, e fim.
    ///
    /// Mostra o caminho e abre a pasta. **Nada é enviado para servidor nenhum** — a
    /// afirmação está na tela porque é uma das perguntas que o responsável de TI do cliente
    /// faz, e a resposta precisa estar escrita, não só verdadeira.
    /// </summary>
    internal sealed class Screen7Save : ScreenBase
    {
        private readonly Panel _corpo = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Fundo };

        private IList<string> _avisos = new List<string>();
        private string _falha;

        internal Screen7Save(SessionState session) : base(session)
        {
            Controls.Add(_corpo);
        }

        internal override string Title => Strings.Tela7Titulo;

        internal override string AdvanceText => Strings.Tela7Encerrar;

        // Voltar da tela final para editar dados manuais e não regravar deixaria o arquivo
        // em disco divergindo do que está na tela. Terminal.
        internal override bool CanGoBack => false;

        internal override void OnEnter()
        {
            _corpo.Controls.Clear();

            if (Session.IsDemo)
            {
                Stack(_corpo, Note(Strings.DemonstracaoTela7, Theme.DemoFundo));
                return;
            }

            // Grava ao ENTRAR na tela, não ao sair da tela 4: o arquivo tem que existir antes
            // de a ferramenta afirmar que existe. Uma vez gravado, não regrava — voltar não é
            // possível a partir daqui, e regravar duplicaria arquivo por navegação.
            if (Session.GeneratedFiles.Count == 0 && _falha == null) Save();

            if (_falha != null)
            {
                Stack(_corpo, Note(string.Format(Strings.Tela7FalhaAoGravar, _falha), Theme.Alto));
                return;
            }

            Stack(_corpo, FileList());

            foreach (var aviso in _avisos)
                Stack(_corpo, Note(string.Format(Strings.Tela7Aviso, aviso), Theme.Medio));

            Stack(_corpo, Note(Strings.Tela7NadaEnviado, Theme.TextoSecundario));
            Stack(_corpo, OpenFolderButton());
        }

        /// <summary>
        /// Grava JSON, HTML e log.
        ///
        /// Falha aqui não fecha a janela nem descarta a coleta: o texto explica o que
        /// aconteceu, e o técnico ainda pode tratar o problema — pasta somente leitura, disco
        /// cheio, antivírus bloqueando a escrita — sem refazer a visita.
        /// </summary>
        private void Save()
        {
            try
            {
                var arquivos = ReportWriter.Write(
                    Session.ToRun(VersaoDaFerramenta()), Session.OutputDirectory);

                foreach (var caminho in arquivos.All) Session.GeneratedFiles.Add(caminho);

                Session.ReportDirectory = arquivos.Directory;
                _avisos = arquivos.Warnings;
            }
            catch (Exception ex)
            {
                _falha = ex.Message;
            }
        }

        /// <summary>
        /// Versão gravada no JSON e no rodapé do relatório. Vem do assembly, que o CI carimba —
        /// é o que permite auditar qual versão produziu qual relatório (doc 02 §8.5).
        /// </summary>
        private static string VersaoDaFerramenta()
        {
            var assembly = Assembly.GetExecutingAssembly();

            var informacional = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

            return informacional != null && !string.IsNullOrWhiteSpace(informacional.InformationalVersion)
                ? informacional.InformationalVersion
                : assembly.GetName().Version.ToString();
        }

        private Control FileList()
        {
            var caixa = new Panel { Dock = DockStyle.Top, BackColor = Theme.FundoCartao, Padding = new Padding(14, 12, 14, 12) };

            var y = 12;
            foreach (var caminho in Session.GeneratedFiles)
            {
                var label = new Label
                {
                    Left = 14,
                    Top = y,
                    Width = 800,
                    Height = 20,
                    Font = Theme.Monoespacada,
                    ForeColor = Theme.Texto,
                    AutoEllipsis = true,
                    Text = caminho
                };
                caixa.Controls.Add(label);
                y += 22;
            }

            caixa.Height = y + 10;
            return caixa;
        }

        private Control Note(string text, Color color)
        {
            var label = TextBlock.Wrapped(text, Theme.Corpo, Theme.Texto, 800, 14, 12);

            var caixa = new Panel
            {
                Dock = DockStyle.Top,
                Height = label.Height + 26,
                BackColor = Theme.FundoCartao
            };

            caixa.Controls.Add(label);
            caixa.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 4, BackColor = color });

            return caixa;
        }

        private Control OpenFolderButton()
        {
            var caixa = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Theme.Fundo };

            var botao = new Button
            {
                Left = 0,
                Top = 12,
                Width = 160,
                Height = 32,
                FlatStyle = FlatStyle.System,
                Text = Strings.Tela7AbrirPasta
            };

            botao.Click += (s, e) => OpenOutputFolder();
            caixa.Controls.Add(botao);

            return caixa;
        }

        private void OpenOutputFolder()
        {
            try
            {
                // A pasta dos arquivos DESTA máquina, não a pasta de saída: com várias máquinas
                // na mesma visita, abrir a raiz obriga o técnico a procurar.
                var pasta = Session.ReportDirectory ?? Session.OutputDirectory;

                if (!Directory.Exists(pasta)) return;

                // UseShellExecute para o Explorer abrir a pasta, e não tentar executá-la.
                Process.Start(new ProcessStartInfo
                {
                    FileName = pasta,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Strings.Tela7FalhaAoAbrir, ex.Message), Strings.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
