using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using EpicoraCheckup.App.Screens;

namespace EpicoraCheckup.App
{
    /// <summary>
    /// Shell do assistente: cabeçalho, faixa de demonstração, área da tela e rodapé.
    ///
    /// Sete telas na especificação, cinco nesta fase — as telas 5 e 6 são de otimização e
    /// pertencem à Fase 5, que só começa depois da Fase 4 validada. O fluxo é
    /// 1 → 2 → 3 → 4 → 7.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private readonly SessionState _session;
        private readonly List<ScreenBase> _screens = new List<ScreenBase>();

        private readonly Label _titulo = new Label();
        private readonly Label _passo = new Label();
        private readonly Panel _conteudo = new Panel();
        private readonly Button _voltar = new Button();
        private readonly Button _avancar = new Button();
        private readonly Label _bloqueio = new Label();

        private int _atual = -1;

        internal MainForm(SessionState session)
        {
            _session = session;

            Text = Strings.AppName;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 660);
            Size = new Size(980, 720);
            BackColor = Theme.Fundo;
            Font = Theme.Corpo;

            if (Marca.Icone != null) Icon = Marca.Icone;

            // A falha da marca não impede nada, mas some se ninguém registrar. O log é o
            // mesmo lugar onde já fica a procedência do relatório — quem for explicar uma
            // captura de tela com a tipografia errada procura ali.
            if (Marca.Falha != null) _session.Log.Warn(Marca.Falha);

            BuildChrome();
            BuildScreens();

            ShowScreen(0);
        }

        // ------------------------------------------------------------ montagem

        private void BuildChrome()
        {
            var rodape = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = Theme.FundoCartao };
            var separadorRodape = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Borda };

            // Segue nativo, e é decisão e não esquecimento: o único botão que sai do desenho
            // do Windows é a ação principal. Repintar também os secundários obrigaria a
            // reproduzir à mão foco, hover e estado desabilitado de cada um — e a tela 3 e a
            // tela 7 têm os seus próprios, que ficariam fora de sintonia no primeiro descuido.
            _voltar.Text = Strings.BotaoVoltar;
            _voltar.Size = new Size(110, 34);
            _voltar.Location = new Point(Theme.Margem, 14);
            _voltar.FlatStyle = FlatStyle.System;
            _voltar.Click += (s, e) => Back();

            _avancar.Text = Strings.BotaoAvancar;
            _avancar.Size = new Size(160, 34);
            _avancar.Click += (s, e) => Advance();
            EstiloPrimario(_avancar);

            _bloqueio.AutoSize = false;
            _bloqueio.Size = new Size(430, 34);
            _bloqueio.TextAlign = ContentAlignment.MiddleRight;
            _bloqueio.Font = Theme.Rotulo;
            _bloqueio.ForeColor = Theme.TextoSecundario;

            rodape.Controls.Add(_voltar);
            rodape.Controls.Add(_bloqueio);
            rodape.Controls.Add(_avancar);
            rodape.Controls.Add(separadorRodape);
            rodape.Resize += (s, e) => PositionFooter(rodape);

            // Cabeçalho na cor da marca, com a mesma composição dos decks comerciais: o
            // logotipo pequeno no alto à esquerda e o título da tela logo abaixo dele.
            //
            // Fundir a faixa da marca com a barra de título da tela, em vez de empilhar as
            // duas, é o que mantém a altura do cromo igual à de antes. Em janela de 660 px
            // de altura mínima, uma faixa a mais sairia da área útil da tela 3.
            var cabecalho = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = Theme.Roxo };

            cabecalho.Paint += (s, e) => Marca.Desenhar(e.Graphics, new Point(Theme.Margem, 18), AlturaDoLogo);

            _titulo.Font = Theme.Titulo;
            _titulo.ForeColor = Color.White;
            _titulo.BackColor = Theme.Roxo;
            _titulo.AutoSize = false;
            _titulo.Location = new Point(Theme.Margem, 52);
            _titulo.Size = new Size(700, 30);

            _passo.Font = Theme.Rotulo;
            _passo.ForeColor = Theme.SobreRoxoSuave;
            _passo.BackColor = Theme.Roxo;
            _passo.AutoSize = false;
            _passo.TextAlign = ContentAlignment.MiddleRight;
            _passo.Size = new Size(200, 20);

            cabecalho.Controls.Add(_titulo);
            cabecalho.Controls.Add(_passo);
            cabecalho.Resize += (s, e) => PositionHeader(cabecalho);

            _conteudo.Dock = DockStyle.Fill;
            _conteudo.BackColor = Theme.Fundo;
            _conteudo.Padding = new Padding(Theme.Margem, 16, Theme.Margem, 16);

            Controls.Add(_conteudo);

            // A faixa de demonstração fica ENTRE o cabeçalho e o conteúdo, visível em todas
            // as telas. Não é dispensável: um relatório de demonstração confundido com real
            // é o pior desfecho possível para este modo existir.
            if (_session.IsDemo)
            {
                var faixa = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 34,
                    BackColor = Theme.DemoFundo,
                    ForeColor = Theme.DemoTexto,
                    Font = Theme.CorpoNegrito,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Text = Strings.DemonstracaoFaixa
                };
                Controls.Add(faixa);
            }

            Controls.Add(cabecalho);
            Controls.Add(rodape);

            PositionHeader(cabecalho);
            PositionFooter(rodape);
        }

        /// <summary>Altura do logotipo no cabeçalho. A largura sai da proporção do arquivo.</summary>
        private const int AlturaDoLogo = 22;

        private void PositionHeader(Control cabecalho)
        {
            // Alinhado com o logotipo, não com o título: os dois são a moldura da marca, e o
            // título é o conteúdo que muda a cada tela.
            _passo.Location = new Point(Math.Max(Theme.Margem, cabecalho.Width - _passo.Width - Theme.Margem), 22);
        }

        /// <summary>
        /// Ação principal na cor da marca.
        ///
        /// <see cref="FlatStyle.Flat"/> e não <see cref="FlatStyle.System"/> porque botão
        /// desenhado pelo tema do Windows ignora <see cref="Control.BackColor"/> — é o
        /// motivo de o botão colorido em WinForms exigir sair do desenho nativo.
        ///
        /// O estado desabilitado precisa de tratamento explícito: o Flat mantém o fundo e só
        /// acinzenta o texto, o que deixaria um botão roxo de aparência ativa com legenda
        /// apagada. Aqui o fundo inteiro esmaece, e o rodapé já diz por que está bloqueado.
        /// </summary>
        private static void EstiloPrimario(Button botao)
        {
            botao.FlatStyle = FlatStyle.Flat;
            botao.FlatAppearance.BorderSize = 0;
            botao.FlatAppearance.MouseOverBackColor = Theme.RoxoProfundo;
            botao.ForeColor = Color.White;
            botao.Font = Theme.CorpoNegrito;
            botao.UseVisualStyleBackColor = false;
            botao.Cursor = Cursors.Hand;

            EventHandler pintar = (s, e) =>
            {
                botao.BackColor = botao.Enabled ? Theme.Roxo : Color.FromArgb(214, 210, 222);
                botao.ForeColor = botao.Enabled ? Color.White : Color.FromArgb(122, 118, 130);
            };

            botao.EnabledChanged += pintar;
            pintar(botao, EventArgs.Empty);
        }

        private void PositionFooter(Control rodape)
        {
            _avancar.Location = new Point(Math.Max(Theme.Margem, rodape.Width - _avancar.Width - Theme.Margem), 14);
            _bloqueio.Location = new Point(Math.Max(Theme.Margem, _avancar.Left - _bloqueio.Width - 12), 14);
        }

        private void BuildScreens()
        {
            _screens.Add(new Screen1Identification(_session));
            _screens.Add(new Screen2Collection(_session));
            _screens.Add(new Screen3Risks(_session));
            _screens.Add(new Screen4ManualData(_session));
            _screens.Add(new Screen7Save(_session));

            foreach (var screen in _screens)
            {
                screen.StateChanged += (s, e) => RefreshFooter();
                screen.AdvanceRequested += (s, e) => RefreshFooter();
            }
        }

        // ------------------------------------------------------------ navegação

        private void ShowScreen(int index)
        {
            if (index < 0 || index >= _screens.Count) return;

            _conteudo.Controls.Clear();

            _atual = index;
            var screen = _screens[index];

            _titulo.Text = screen.Title;
            _passo.Text = $"{index + 1} de {_screens.Count}";

            _conteudo.Controls.Add(screen);
            screen.OnEnter();

            RefreshFooter();
        }

        private void RefreshFooter()
        {
            if (_atual < 0) return;

            var screen = _screens[_atual];

            _voltar.Enabled = _atual > 0 && screen.CanGoBack;
            _voltar.Visible = _atual > 0;

            _avancar.Text = screen.AdvanceText;
            _avancar.Enabled = screen.CanAdvance;

            _bloqueio.Text = screen.CanAdvance ? string.Empty : screen.BlockedReason ?? string.Empty;
        }

        private void Back()
        {
            if (_atual <= 0) return;
            ShowScreen(_atual - 1);
        }

        private void Advance()
        {
            var screen = _screens[_atual];

            if (!screen.TryLeave()) return;

            if (_atual == _screens.Count - 1)
            {
                Close();
                return;
            }

            ShowScreen(_atual + 1);
        }
    }
}
