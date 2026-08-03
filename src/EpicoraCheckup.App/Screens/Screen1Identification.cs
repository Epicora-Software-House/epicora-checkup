using System;
using System.Drawing;
using System.Windows.Forms;
using EpicoraCheckup.App.Controls;

namespace EpicoraCheckup.App.Screens
{
    /// <summary>
    /// Tela 1 — identificação do diagnóstico.
    ///
    /// Além dos quatro campos, tem duas coisas que não são decoração:
    ///
    ///  - **O aviso de privacidade**, permanente e não dispensável (doc 01 §5). Fica visível
    ///    para que o responsável de TI do cliente possa ler por cima do ombro do técnico.
    ///  - **O estado de elevação**, declarado ANTES de iniciar. Descobrir na tela 3 que
    ///    metade das fontes privilegiadas ficou indeterminada é retrabalho de visita — o
    ///    técnico precisa saber agora, enquanto ainda pode pedir a senha ao TI.
    /// </summary>
    internal sealed class Screen1Identification : ScreenBase
    {
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
