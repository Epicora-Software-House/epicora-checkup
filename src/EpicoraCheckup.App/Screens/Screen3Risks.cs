using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using EpicoraCheckup.App.Controls;
using EpicoraCheckup.Core.Model;

namespace EpicoraCheckup.App.Screens
{
    /// <summary>
    /// Tela 3 — riscos e pontos de atenção. A tela mais importante da ferramenta.
    ///
    /// Três blocos, e a separação entre o segundo e o terceiro é o princípio 3 do doc 01
    /// tornado visível: o que não pudemos verificar aparece **em bloco próprio**, com o
    /// motivo, e nunca misturado aos achados. Um cliente que vê "não foi possível verificar
    /// se há BitLocker" entende. Um cliente que vê "sem BitLocker" numa máquina criptografada
    /// perde a confiança no relatório inteiro.
    /// </summary>
    internal sealed class Screen3Risks : ScreenBase
    {
        /// <summary>
        /// Largura de conteúdo fixa. A janela tem MinimumSize de 900, então cabe sempre.
        /// Cartão com largura fixa permite calcular a altura do texto quebrado na montagem,
        /// que é o que WinForms não dá de graça sem designer. Reflow ao redimensionar ficou
        /// de fora de propósito — custa refazer o layout inteiro a cada Resize e a tela é
        /// para ser lida, não manipulada.
        /// </summary>
        private const int LarguraConteudo = 830;

        private readonly Panel _corpo = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Fundo };

        internal Screen3Risks(SessionState session) : base(session)
        {
            Controls.Add(_corpo);
        }

        internal override string Title => Strings.Tela3Titulo;

        // A tela anterior é a coleta, que não se repete. Voltar dali não faria nada útil.
        internal override bool CanGoBack => false;

        internal override void OnEnter()
        {
            // Reconstrói a cada entrada: ao voltar da tela 4, uma marcação de falso positivo
            // feita aqui precisa continuar visível.
            _corpo.Controls.Clear();
            Build();
        }

        private void Build()
        {
            var achados = Session.Findings
                .Where(f => f.State == RuleState.NonCompliant)
                .ToList();

            var indeterminados = Session.Findings
                .Where(f => f.State == RuleState.Indeterminate)
                .ToList();

            // Empilhamento: cada controle inserido vai para o índice 0 do painel, e o layout
            // de Dock percorre a coleção do índice mais alto para o zero. O efeito é que a
            // ORDEM DE INSERÇÃO é a ordem visual de cima para baixo. Então monta-se na ordem
            // de leitura, que é o que se quer.
            AddScoreCard();
            AddFindings(achados);
            AddIndeterminateBlock(indeterminados);
        }

        // ------------------------------------------------------------ score

        private void AddScoreCard()
        {
            var score = Session.Score;

            var cartao = new Panel
            {
                Dock = DockStyle.Top,
                Height = 118,
                BackColor = Theme.FundoCartao,
                Padding = new Padding(18, 14, 18, 14)
            };

            if (score == null)
            {
                cartao.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    Font = Theme.Corpo,
                    ForeColor = Theme.TextoSecundario,
                    Text = Strings.Tela3SemAchados
                });
                Stack(_corpo, cartao);
                return;
            }

            var cor = Theme.CorDaFaixa(score.Band);

            var numero = new Label
            {
                Left = 18,
                Top = 18,
                Width = 130,
                Height = 62,
                Font = Theme.ScoreGrande,
                ForeColor = cor,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = score.Value.ToString()
            };

            var rotuloScore = new Label
            {
                Left = 20,
                Top = 82,
                Width = 220,
                Height = 18,
                Font = Theme.Rotulo,
                ForeColor = Theme.TextoSecundario,
                Text = Strings.Tela3Score + " — " + Theme.NomeDaFaixa(score.Band)
            };

            var veredito = new Label
            {
                Left = 200,
                Top = 26,
                Width = 380,
                Height = 34,
                Font = Theme.Titulo,
                ForeColor = Theme.Texto,
                Text = Theme.NomeDoVeredito(score.Verdict)
            };

            var rotuloVeredito = new Label
            {
                Left = 202,
                Top = 62,
                Width = 380,
                Height = 18,
                Font = Theme.Rotulo,
                ForeColor = Theme.TextoSecundario,
                Text = Strings.Tela3Veredito
            };

            var faixa = new Panel { Dock = DockStyle.Left, Width = 6, BackColor = cor };

            cartao.Controls.Add(numero);
            cartao.Controls.Add(rotuloScore);
            cartao.Controls.Add(veredito);
            cartao.Controls.Add(rotuloVeredito);

            // Determinado por quais regras: permite ao técnico contestar a REGRA, e não o
            // número, quando o veredito parecer errado.
            if (score.VerdictDrivenBy != null && score.VerdictDrivenBy.Count > 0)
            {
                cartao.Controls.Add(new Label
                {
                    Left = 202,
                    Top = 82,
                    Width = 560,
                    Height = 18,
                    Font = Theme.Rotulo,
                    ForeColor = Theme.TextoSecundario,
                    Text = "determinado por: " + string.Join(", ", score.VerdictDrivenBy)
                });
            }

            cartao.Controls.Add(faixa);

            Stack(_corpo, cartao);
        }

        // ------------------------------------------------------------ achados

        private void AddFindings(IList<Finding> achados)
        {
            if (achados.Count == 0)
            {
                AddSpacer(10);
                AddNote(Strings.Tela3SemAchados, Theme.BandaVerde, Theme.CorpoNegrito);
                return;
            }

            // Já vêm ordenados por severidade e depois por id pelo motor, e GroupBy do LINQ
            // preserva a ordem de aparição — então não há o que reordenar dentro do grupo.
            foreach (var grupo in achados.GroupBy(f => f.Severity).OrderBy(g => (int)g.Key))
            {
                AddSectionTitle(Theme.NomeDaSeveridade(grupo.Key), Theme.CorDaSeveridade(grupo.Key), grupo.Count());

                foreach (var achado in grupo)
                    AddCard(achado);
            }
        }

        private void AddCard(Finding achado)
        {
            var cartao = new Panel
            {
                Dock = DockStyle.Top,
                BackColor = Theme.FundoCartao,
                Padding = new Padding(0)
            };

            var y = 12;
            var larguraTexto = LarguraConteudo - 28;

            var titulo = TextBlock.Wrapped(achado.Title, Theme.CorpoNegrito, Theme.Texto, larguraTexto, 14, y);
            cartao.Controls.Add(titulo);
            y += titulo.Height + 6;

            // clientText nulo é regra sem texto aprovado pelo comercial. Não inventar texto
            // aqui: mostrar o id, que é o que permite rastrear e cobrar a aprovação.
            var texto = string.IsNullOrWhiteSpace(achado.ClientText)
                ? $"({achado.RuleId} — texto de cliente ainda não aprovado)"
                : achado.ClientText;

            var corpo = TextBlock.Wrapped(texto, Theme.Corpo, Theme.Texto, larguraTexto, 14, y);
            cartao.Controls.Add(corpo);
            y += corpo.Height + 6;

            if (!string.IsNullOrWhiteSpace(achado.RecommendedAction))
            {
                var acao = TextBlock.Wrapped(Strings.Tela3AcaoRecomendada + " " + achado.RecommendedAction,
                    Theme.Rotulo, Theme.TextoSecundario, larguraTexto, 14, y);
                cartao.Controls.Add(acao);
                y += acao.Height + 6;
            }

            var marcar = new Button
            {
                Left = 14,
                Top = y,
                Width = 210,
                Height = 28,
                FlatStyle = FlatStyle.System,
                Font = Theme.Rotulo,
                Text = achado.MarkedFalsePositive ? Strings.Tela3FalsoPositivoMarcado : Strings.Tela3MarcarFalsoPositivo,
                Enabled = !achado.MarkedFalsePositive
            };
            marcar.Click += (s, e) => MarkFalsePositive(achado, marcar, cartao);

            cartao.Controls.Add(marcar);
            y += marcar.Height + 12;

            cartao.Height = y;

            var barra = new Panel
            {
                Dock = DockStyle.Left,
                Width = 4,
                BackColor = achado.MarkedFalsePositive ? Theme.Indeterminado : Theme.CorDaSeveridade(achado.Severity)
            };
            cartao.Controls.Add(barra);

            if (achado.MarkedFalsePositive) DimCard(cartao);

            Stack(_corpo, cartao);

            AddSpacer(6);
        }

        private void MarkFalsePositive(Finding achado, Button botao, Panel cartao)
        {
            using (var dialogo = new JustificationDialog(achado.Title))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK) return;

                if (string.IsNullOrWhiteSpace(dialogo.Justification))
                {
                    MessageBox.Show(Strings.Tela3FalsoPositivoExigeJustificativa, Strings.AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                achado.MarkedFalsePositive = true;
                achado.FalsePositiveJustification = dialogo.Justification;
            }

            // O SCORE NÃO É RECALCULADO, e isso é deliberado. A marcação registra que o
            // técnico discorda da regra, para que a regra seja corrigida — não é uma alça
            // para ajustar o número. Se mexesse no score, o índice deixaria de medir a
            // máquina e passaria a medir a opinião de quem operou a ferramenta.
            botao.Text = Strings.Tela3FalsoPositivoMarcado;
            botao.Enabled = false;
            DimCard(cartao);
        }

        private static void DimCard(Control cartao)
        {
            foreach (Control filho in cartao.Controls)
            {
                var barra = filho as Panel;
                if (barra != null && barra.Dock == DockStyle.Left) { barra.BackColor = Theme.Indeterminado; continue; }

                var label = filho as Label;
                if (label != null) label.ForeColor = Theme.TextoSecundario;
            }
        }

        // ------------------------------------------------------------ indeterminados

        private void AddIndeterminateBlock(IList<Finding> indeterminados)
        {
            if (indeterminados.Count == 0) return;

            AddSpacer(10);
            AddSectionTitle(Strings.Tela3NaoVerificado, Theme.Indeterminado, indeterminados.Count);
            AddNote(Strings.Tela3NaoVerificadoExplicacao, Theme.TextoSecundario, Theme.Rotulo);

            foreach (var achado in indeterminados)
            {
                var cartao = new Panel { Dock = DockStyle.Top, BackColor = Theme.FundoCartao };

                var y = 10;
                var larguraTexto = LarguraConteudo - 28;

                var titulo = TextBlock.Wrapped(achado.Title, Theme.Corpo, Theme.Texto, larguraTexto, 14, y);
                cartao.Controls.Add(titulo);
                y += titulo.Height + 4;

                var motivo = TextBlock.Wrapped(achado.IndeterminateReason ?? "(sem motivo registrado)",
                    Theme.Rotulo, Theme.Indeterminado, larguraTexto, 14, y);
                cartao.Controls.Add(motivo);
                y += motivo.Height + 10;

                cartao.Height = y;
                cartao.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 4, BackColor = Theme.Indeterminado });

                Stack(_corpo, cartao);

                AddSpacer(4);
            }
        }

        // ------------------------------------------------------------ montagem auxiliar

        private void AddSectionTitle(string text, Color color, int count)
        {
            var faixa = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Theme.Fundo };

            faixa.Controls.Add(new Label
            {
                Left = 2,
                Top = 10,
                Width = LarguraConteudo,
                Height = 20,
                Font = Theme.CorpoNegrito,
                ForeColor = color,
                Text = $"{text} ({count})"
            });

            Stack(_corpo, faixa);
        }

        private void AddNote(string text, Color color, Font font)
        {
            var label = TextBlock.Wrapped(text, font, color, LarguraConteudo - 8, 2, 4);

            var caixa = new Panel { Dock = DockStyle.Top, Height = label.Height + 12, BackColor = Theme.Fundo };
            caixa.Controls.Add(label);

            Stack(_corpo, caixa);
        }

        private void AddSpacer(int height)
        {
            var espaco = new Panel { Dock = DockStyle.Top, Height = height, BackColor = Theme.Fundo };
            Stack(_corpo, espaco);
        }
    }
}
