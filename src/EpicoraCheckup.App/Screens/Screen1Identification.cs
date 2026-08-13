using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using EpicoraCheckup.App.Controls;
using EpicoraCheckup.Core.Update;

namespace EpicoraCheckup.App.Screens
{
    /// <summary>
    /// Tela 1 — identificação do diagnóstico.
    ///
    /// Além dos quatro campos, tem três coisas que não são decoração:
    ///
    ///  - **O aviso de privacidade**, permanente e não dispensável (doc 01 §5). Fica visível
    ///    para que o responsável de TI do cliente possa ler por cima do ombro do técnico.
    ///  - **O estado de elevação**, declarado ANTES de iniciar. Descobrir na tela 3 que
    ///    metade das fontes privilegiadas ficou indeterminada é retrabalho de visita — o
    ///    técnico precisa saber agora, enquanto ainda pode pedir a senha ao TI.
    ///  - **A verificação de versão** (doc 01 §4, ADR-014), que roda aqui e não depois: é a
    ///    última janela em que trocar de binário custa só um download. Depois da coleta, o
    ///    relatório já foi produzido pelo critério antigo.
    /// </summary>
    internal sealed class Screen1Identification : ScreenBase
    {
        private bool _versionChecked;

        internal Screen1Identification(SessionState session) : base(session)
        {
            Build();
        }

        internal override string Title => Strings.Tela1Titulo;

        internal override string AdvanceText => Strings.Tela1Iniciar;

        internal override bool CanAdvance => Session.Identification.IsComplete;

        internal override string BlockedReason => Strings.CampoObrigatorio;

        internal override bool TryLeave()
        {
            // Persistido para não redigitar na próxima máquina da mesma visita.
            Session.SaveIdentification();
            return true;
        }

        /// <summary>
        /// Dispara a verificação de versão quando o handle existe.
        ///
        /// Não em <c>OnEnter</c>, que é o gancho natural: a tela 1 entra durante o construtor
        /// do <see cref="MainForm"/>, antes de <c>Application.Run</c>, e ali não há
        /// <c>SynchronizationContext</c> de WinForms garantido — a continuação do <c>await</c>
        /// voltaria numa thread de pool, tocando controle e o log de fora da thread da UI.
        /// Criação de handle acontece na exibição da janela, com a bomba de mensagens rodando.
        /// </summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (_versionChecked) return;
            _versionChecked = true;

            CheckVersion();
        }

        // ------------------------------------------------------------ verificação de versão

        /// <summary>
        /// Consulta o release mais recente fora da thread da UI e, se houver versão nova,
        /// mostra o aviso.
        ///
        /// <c>async void</c> porque não há quem espere por isto — e não pode haver. A janela
        /// abre, o técnico começa a digitar, e o aviso aparece quando aparecer. Se não
        /// aparecer, o diagnóstico corre igual (doc 02 §8.3).
        /// </summary>
        private async void CheckVersion()
        {
            var installed = ToolIdentity.Version;

            var result = await Task.Run(() => UpdateCheck.Run(
                installed,
                () => ReleaseFeed.Fetch(UpdateCheck.LatestReleaseUrl, UpdateCheck.Timeout)));

            // Doc 02 §9: o resultado da verificação de versão é registro obrigatório. Aqui, e
            // não dentro da verificação, porque o log é gravado por esta thread — a da UI —
            // e RunLog acumula numa lista que não é preparada para dois escritores.
            if (result.State == UpdateState.NotChecked)
                Session.Log.Warn("verificação de versão: " + result.Detail);
            else
                Session.Log.Info("verificação de versão: " + result.Detail);

            if (result.State != UpdateState.Outdated) return;
            if (IsDisposed || !IsHandleCreated) return;

            Stack(this, BuildOutdatedNotice(result));
        }

        /// <summary>
        /// Aviso de versão desatualizada: informa e oferece o download, sem impedir nada.
        ///
        /// Não bloqueia o botão de iniciar de propósito. Pode não haver como baixar naquele
        /// momento — cliente sem rede liberada, técnico no meio de uma visita —, e um
        /// diagnóstico com a matriz de duas semanas atrás vale mais que nenhum diagnóstico.
        /// </summary>
        private Control BuildOutdatedNotice(UpdateCheckResult result)
        {
            var caixa = new Panel
            {
                Dock = DockStyle.Top,
                Height = 98,
                BackColor = Theme.FundoCartao,
                Padding = new Padding(14, 12, 14, 12),
                Margin = new Padding(0, 12, 0, 0)
            };

            var texto = new Label
            {
                Dock = DockStyle.Top,
                Height = 52,
                Font = Theme.Corpo,
                ForeColor = Theme.Texto,
                Text = string.Format(Strings.Tela1VersaoDesatualizada, result.InstalledVersion, result.LatestVersion)
            };

            var link = new LinkLabel
            {
                Dock = DockStyle.Top,
                Height = 20,
                Font = Theme.Corpo,
                LinkColor = Theme.Link,
                ActiveLinkColor = Theme.Link,
                VisitedLinkColor = Theme.Link,
                Text = Strings.Tela1VersaoBaixar
            };

            link.LinkClicked += (s, e) => OpenDownloadPage();

            // Docking é aplicado do índice mais alto para o zero, então inserir na ordem
            // inversa da leitura é o que põe o texto acima do link.
            caixa.Controls.Add(link);
            caixa.Controls.Add(texto);

            caixa.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 4, BackColor = Theme.BandaAmarela });

            caixa.Paint += (s, e) => ControlPaint.DrawBorder(e.Graphics, caixa.ClientRectangle, Theme.Borda, ButtonBorderStyle.Solid);

            return caixa;
        }

        private void OpenDownloadPage()
        {
            try
            {
                // Abre no navegador da máquina, que é de onde o técnico baixou a cópia atual.
                Process.Start(UpdateCheck.DownloadUrl);
            }
            catch (Exception ex)
            {
                // Sem navegador registrado, ou bloqueado por política. O link não é o único
                // caminho: a URL está no log e no procedimento do técnico.
                Session.Log.Warn("não foi possível abrir o navegador para o download: " + ex.Message);
            }
        }

        private void Build()
        {
            var campos = new FieldStack { Padding = new Padding(0, 0, 0, 12) };

            campos.AddText(Strings.Tela1Tecnico, Session.Identification.Technician, true,
                (s, e) => { Session.Identification.Technician = ((TextBox)s).Text; RaiseStateChanged(); });

            campos.AddText(Strings.Tela1Cliente, Session.Identification.Client, true,
                (s, e) => { Session.Identification.Client = ((TextBox)s).Text; RaiseStateChanged(); });

            campos.AddText(Strings.Tela1Unidade, Session.Identification.Unit, false,
                (s, e) => Session.Identification.Unit = ((TextBox)s).Text);

            campos.AddText(Strings.Tela1Diagnostico, Session.Identification.DiagnosticId, true,
                (s, e) => { Session.Identification.DiagnosticId = ((TextBox)s).Text; RaiseStateChanged(); });

            // Propriedade da VISITA, não da máquina — o protótipo PowerShell já a recebe como
            // parâmetro de invocação. Fica aqui, e não na tela 4, porque a avaliação das regras
            // acontece na tela 2, antes de a tela 4 existir: OS-004 precisa dela até lá.
            campos.AddCheckbox(Strings.Tela1Corporativo, Session.Identification.CorporateEnvironment,
                (s, e) => Session.Identification.CorporateEnvironment = ((CheckBox)s).Checked,
                Strings.Tela1CorporativoDica);

            Stack(this, campos);
            Stack(this, BuildPrivacyNotice());
            Stack(this, BuildElevationNotice());
        }

        private Control BuildPrivacyNotice()
        {
            var caixa = new Panel
            {
                Dock = DockStyle.Top,
                Height = 74,
                BackColor = Theme.FundoCartao,
                Padding = new Padding(14, 12, 14, 12),
                Margin = new Padding(0, 0, 0, 12)
            };

            caixa.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Font = Theme.Corpo,
                ForeColor = Theme.Texto,
                Text = Strings.Tela1AvisoPrivacidade
            });

            caixa.Paint += (s, e) => ControlPaint.DrawBorder(e.Graphics, caixa.ClientRectangle, Theme.Borda, ButtonBorderStyle.Solid);

            return caixa;
        }

        private Control BuildElevationNotice()
        {
            var elevado = Session.IsElevated;

            var caixa = new Panel
            {
                Dock = DockStyle.Top,
                Height = 74,
                BackColor = Theme.FundoCartao,
                Padding = new Padding(14, 12, 14, 12)
            };

            caixa.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Font = Theme.Corpo,
                ForeColor = elevado ? Theme.Texto : Theme.Medio,
                Text = elevado ? Strings.Tela1Elevado : Strings.Tela1NaoElevado
            });

            var faixa = new Panel
            {
                Dock = DockStyle.Left,
                Width = 4,
                BackColor = elevado ? Theme.BandaVerde : Theme.Medio
            };
            caixa.Controls.Add(faixa);

            return caixa;
        }
    }
}
