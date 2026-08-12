using System;
using System.Drawing;
using System.Windows.Forms;

namespace EpicoraCheckup.App.Controls
{
    /// <summary>
    /// Empilha campos rotulados verticalmente.
    ///
    /// As telas 1 e 4 são formulários, e escrever WinForms à mão sem designer (ADR-010) faria
    /// cada campo custar seis linhas de posicionamento. Isto reduz a um método por campo, e
    /// mantém alinhamento e espaçamento consistentes entre as duas telas — que é o que o
    /// designer daria de graça e que passa a ser responsabilidade nossa.
    /// </summary>
    internal sealed class FieldStack : Panel
    {
        private int _y;

        internal FieldStack()
        {
            Dock = DockStyle.Top;
            BackColor = Color.Transparent;
            _y = 0;
            Height = 0;
        }

        internal TextBox AddText(string label, string value, bool required, EventHandler onChanged, string hint = null)
        {
            AddLabel(label, required);

            var box = new TextBox
            {
                Left = 0,
                Top = _y,
                Width = 520,
                Font = Theme.Corpo,
                Text = value ?? string.Empty
            };

            if (onChanged != null) box.TextChanged += onChanged;

            Controls.Add(box);
            Grow(box.Height + (hint == null ? Theme.EspacoEntreCampos + 6 : 2));

            if (hint != null) AddHint(hint);

            return box;
        }

        internal TextBox AddMultiline(string label, string value, EventHandler onChanged, int lines, string hint = null)
        {
            AddLabel(label, false);

            var box = new TextBox
            {
                Left = 0,
                Top = _y,
                Width = 520,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Height = 20 * lines,
                Font = Theme.Corpo,
                Text = value ?? string.Empty
            };

            if (onChanged != null) box.TextChanged += onChanged;

            Controls.Add(box);
            Grow(box.Height + (hint == null ? Theme.EspacoEntreCampos + 6 : 2));

            if (hint != null) AddHint(hint);

            return box;
        }

        internal CheckBox AddCheckbox(string label, bool value, EventHandler onChanged, string hint = null)
        {
            var box = new CheckBox
            {
                Left = 0,
                Top = _y,
                Width = 520,
                Height = 22,
                Font = Theme.Corpo,
                ForeColor = Theme.Texto,
                Checked = value,
                Text = label
            };

            if (onChanged != null) box.CheckedChanged += onChanged;

            Controls.Add(box);
            Grow(box.Height + (hint == null ? Theme.EspacoEntreCampos + 6 : 2));

            if (hint != null) AddHint(hint);

            return box;
        }

        private void AddLabel(string text, bool required)
        {
            var label = new Label
            {
                Left = 0,
                Top = _y,
                Width = 520,
                AutoSize = false,
                Height = 18,
                Font = Theme.Rotulo,
                ForeColor = Theme.TextoSecundario,
                // O asterisco marca obrigatório. A tela também explica em texto: só o
                // asterisco não comunica para quem nunca usou a ferramenta.
                Text = required ? text + " *" : text
            };

            Controls.Add(label);
            Grow(label.Height + 2);
        }

        private void AddHint(string hint)
        {
            var label = new Label
            {
                Left = 0,
                Top = _y,
                Width = 520,
                AutoSize = false,
                Height = 16,
                Font = Theme.Rotulo,
                ForeColor = Theme.TextoSecundario,
                Text = hint
            };

            Controls.Add(label);
            Grow(label.Height + Theme.EspacoEntreCampos + 4);
        }

        private void Grow(int by)
        {
            _y += by;
            Height = _y + Padding.Vertical;
        }
    }
}
