using System;
using System.Drawing;
using System.Windows.Forms;

namespace EpicoraCheckup.App.Controls
{
    /// <summary>
    /// Pede a justificativa para marcar um achado como falso positivo.
    ///
    /// A justificativa é OBRIGATÓRIA. Marcar sem justificar produziria um dado inútil: o
    /// valor da marcação é permitir corrigir a regra depois, e para isso alguém precisa
    /// saber o que a regra errou. Sem texto, fica só "o técnico discordou".
    /// </summary>
    internal sealed class JustificationDialog : Form
    {
        private readonly TextBox _texto = new TextBox();
        private readonly Button _confirmar = new Button();

        internal JustificationDialog(string findingTitle)
        {
            Text = Strings.Tela3FalsoPositivoTitulo;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(560, 260);
            BackColor = Theme.FundoCartao;
            Font = Theme.Corpo;

            var titulo = new Label
            {
                Left = 16,
                Top = 16,
                Width = 528,
                Height = 40,
                Font = Theme.CorpoNegrito,
                ForeColor = Theme.Texto,
                Text = findingTitle
            };

            var pergunta = new Label
            {
                Left = 16,
                Top = 60,
                Width = 528,
                Height = 34,
                Font = Theme.Rotulo,
                ForeColor = Theme.TextoSecundario,
                Text = Strings.Tela3FalsoPositivoJustificativa
            };

            _texto.Left = 16;
            _texto.Top = 98;
            _texto.Width = 528;
            _texto.Height = 90;
            _texto.Multiline = true;
            _texto.ScrollBars = ScrollBars.Vertical;
            _texto.TextChanged += (s, e) => _confirmar.Enabled = HasJustification;

            _confirmar.Text = Strings.Tela3MarcarFalsoPositivo;
            _confirmar.Left = 344;
            _confirmar.Top = 202;
            _confirmar.Width = 200;
            _confirmar.Height = 32;
            _confirmar.FlatStyle = FlatStyle.System;
            _confirmar.Enabled = false;
            _confirmar.DialogResult = DialogResult.OK;

            var cancelar = new Button
            {
                Text = Strings.BotaoCancelar,
                Left = 236,
                Top = 202,
                Width = 100,
                Height = 32,
                FlatStyle = FlatStyle.System,
                DialogResult = DialogResult.Cancel
            };

            Controls.Add(titulo);
            Controls.Add(pergunta);
            Controls.Add(_texto);
            Controls.Add(_confirmar);
            Controls.Add(cancelar);

            AcceptButton = _confirmar;
            CancelButton = cancelar;
        }

        private bool HasJustification => !string.IsNullOrWhiteSpace(_texto.Text);

        internal string Justification => _texto.Text.Trim();
    }
}
