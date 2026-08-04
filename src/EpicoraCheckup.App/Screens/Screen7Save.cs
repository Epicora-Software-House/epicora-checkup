using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using EpicoraCheckup.App.Controls;
using EpicoraCheckup.Reporting;

namespace EpicoraCheckup.App.Screens
{
    /// <summary>
    /// Tela 7 — arquivos gerados, e fim.
    ///
    /// É aqui que a gravação acontece, e não antes: o documento precisa dos dados manuais da
    /// tela 4, e escrever mais cedo produziria um arquivo que diverge do que está na tela.
    ///
    /// Mostra o caminho e abre a pasta. **Nada é enviado para servidor nenhum** — a
    /// afirmação está na tela porque é uma das perguntas que o responsável de TI do cliente
    /// faz, e a resposta precisa estar escrita, não só verdadeira.
    /// </summary>
    internal sealed class Screen7Save : ScreenBase
    {
        private readonly Panel _corpo = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Fundo };

        private string _pastaDoCliente;
        private string _erroDeGravacao;

        internal Screen7Save(SessionState session) : base(session)
        {
            Controls.Add(_corpo);
        }

        internal override string Title => Strings.Tela7Titulo;

        internal override string AdvanceText => Strings.Tela7Encerrar;

        // Voltar da tela final para editar dados manuais deixaria o arquivo em disco
        // divergindo do que está na tela. Terminal.
        internal override bool CanGoBack => false;

        internal override void OnEnter()
        {
            _corpo.Controls.Clear();

            // Demonstração NÃO grava, em nenhuma circunstância. Um relatório derivado de
            // fixture não pode circular, e depois que o arquivo existe nenhum aviso na tela
            // impede alguém de entregá-lo.
            if (Session.IsDemo)
            {
                Stack(_corpo, Note(Strings.DemonstracaoTela7, Theme.DemoFundo));
                return;
            }

            if (Session.CollectorResults.Count == 0)
            {
                Stack(_corpo, Note(Strings.ColetoresNaoPortados, Theme.Medio));
                return;
            }

            if (Session.GeneratedFiles.Count == 0 && _erroDeGravacao == null) WriteReports();

            if (_erroDeGravacao != null)
            {
                Stack(_corpo, Note(string.Format(Strings.Tela7FalhaAoGravar, _erroDeGravacao), Theme.Critico));
                return;
            }

            Stack(_corpo, FileList());
            Stack(_corpo, Note(Strings.Tela7NadaEnviado, Theme.TextoSecundario));
            Stack(_corpo, OpenFolderButton());
        }

        private void WriteReports()
        {
            try
            {
                Session.Log.Info("montando documento do schema 1.0");

                var documento = CheckupDocument.Build(ReportInputFactory.From(Session, withEvaluation: true));

                var escritos = ReportWriter.WriteAll(
                    documento,
                    Session.OutputDirectory,
                    Session.Identification.Client,
                    Session.Log,
                    Session.FinishedAt ?? DateTimeOffset.Now);

                foreach (var caminho in escritos)
                    Session.GeneratedFiles.Add(caminho);

                _pastaDoCliente = Path.GetDirectoryName(escritos[0]);
            }
            catch (Exception ex)
            {
                // Falhar aqui é o pior momento: a coleta acabou e o dado existe só em memória.
                // Então a mensagem diz o que houve, em vez de sumir com a tela.
                Session.Log.Error("falha ao gravar o relatório", ex);
                _erroDeGravacao = ex.Message;
            }
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
            var pasta = _pastaDoCliente ?? Session.OutputDirectory;

            try
            {
                if (!Directory.Exists(pasta)) return;

                // UseShellExecute para o Explorer abrir a pasta, e não tentar executá-la.
                Process.Start(new ProcessStartInfo { FileName = pasta, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Strings.Tela7FalhaAoAbrir, ex.Message), Strings.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
