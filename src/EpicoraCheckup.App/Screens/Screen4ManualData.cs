using System.Drawing;
using System.Windows.Forms;
using EpicoraCheckup.App.Controls;

namespace EpicoraCheckup.App.Screens
{
    /// <summary>
    /// Tela 4 — dados manuais no padrão do cliente.
    ///
    /// Os três primeiros campos são obrigatórios (doc 01 §5). Os outros quatro não, e é
    /// proposital: <c>physicalCondition</c> e <c>notes</c> são onde entra o que o usuário
    /// relatou em voz alta, e exigir preenchimento faria o técnico digitar "ok" para passar.
    /// </summary>
    internal sealed class Screen4ManualData : ScreenBase
    {
        internal Screen4ManualData(SessionState session) : base(session)
        {
            Build();
        }

        internal override string Title => Strings.Tela4Titulo;

        internal override bool CanAdvance => Session.Manual.IsComplete;

        internal override string BlockedReason => Strings.CampoObrigatorio;

        private void Build()
        {
            var campos = new FieldStack();

            campos.AddText(Strings.Tela4Etiqueta, Session.Manual.MachineLabel, true,
                (s, e) => { Session.Manual.MachineLabel = ((TextBox)s).Text; RaiseStateChanged(); });

            campos.AddText(Strings.Tela4Responsavel, Session.Manual.Responsible, true,
                (s, e) => { Session.Manual.Responsible = ((TextBox)s).Text; RaiseStateChanged(); });

            campos.AddText(Strings.Tela4Setor, Session.Manual.Department, true,
                (s, e) => { Session.Manual.Department = ((TextBox)s).Text; RaiseStateChanged(); });

            campos.AddText(Strings.Tela4Localizacao, Session.Manual.PhysicalLocation, false,
                (s, e) => Session.Manual.PhysicalLocation = ((TextBox)s).Text);

            campos.AddText(Strings.Tela4Patrimonio, Session.Manual.AssetTag, false,
                (s, e) => Session.Manual.AssetTag = ((TextBox)s).Text);

            campos.AddMultiline(Strings.Tela4CondicaoFisica, Session.Manual.PhysicalCondition,
                (s, e) => Session.Manual.PhysicalCondition = ((TextBox)s).Text, 3, Strings.Tela4CondicaoFisicaDica);

            campos.AddMultiline(Strings.Tela4Observacoes, Session.Manual.Notes,
                (s, e) => Session.Manual.Notes = ((TextBox)s).Text, 3, Strings.Tela4ObservacoesDica);

            var explicacao = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                Font = Theme.Rotulo,
                ForeColor = Theme.TextoSecundario,
                Text = Strings.Tela4Explicacao
            };

            Stack(this, explicacao);
            Stack(this, campos);
        }
    }
}
