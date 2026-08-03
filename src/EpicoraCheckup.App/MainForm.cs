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

            BuildChrome();
            BuildScreens();

            ShowScreen(0);
        }

        // ------------------------------------------------------------ montagem

        private void BuildChrome()
        {
            var rodape = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = Theme.FundoCartao };
            var separadorRodape = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Borda };

            _voltar.Text = Strings.BotaoVoltar;
            _voltar.Size = new Size(110, 34);
            _voltar.Location = new Point(Theme.Margem, 14);
            _voltar.FlatStyle = FlatStyle.System;
            _voltar.Click += (s, e) => Back();

            _avancar.Text = Strings.BotaoAvancar;
            _avancar.Size = new Size(160, 34);
            _avancar.FlatStyle = FlatStyle.System;
            _avancar.Click += (s, e) => Advance();

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

            var cabecalho = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Theme.FundoCartao };
            var separadorCabecalho = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Borda };

            _titulo.Font = Theme.Titulo;
            _titulo.ForeColor = Theme.Texto;
            _titulo.AutoSize = false;
            _titulo.Location = new Point(Theme.Margem, 18);
            _titulo.Size = new Size(700, 30);

            _passo.Font = Theme.Rotulo;
            _passo.ForeColor = Theme.TextoSecundario;
            _passo.AutoSize = false;
            _passo.TextAlign = ContentAlignment.MiddleRight;
            _passo.Size = new Size(200, 20);

            cabecalho.Controls.Add(_titulo);
            cabecalho.Controls.Add(_passo);
            cabecalho.Controls.Add(separadorCabecalho);
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

        private void PositionHeader(Control cabecalho)
        {
            _passo.Location = new Point(Math.Max(Theme.Margem, cabecalho.Width - _passo.Width - Theme.Margem), 26);
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
