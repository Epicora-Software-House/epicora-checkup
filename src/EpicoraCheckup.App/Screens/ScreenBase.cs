using System;
using System.Windows.Forms;

namespace EpicoraCheckup.App.Screens
{
    /// <summary>
    /// Base das telas do assistente.
    ///
    /// Cada tela é um <see cref="UserControl"/> hospedado pelo <see cref="MainForm"/>, que
    /// cuida de cabeçalho, rodapé e navegação. A tela não sabe qual é a próxima nem manipula
    /// os botões do rodapé — pede, e o shell decide.
    /// </summary>
    internal abstract class ScreenBase : UserControl
    {
        protected ScreenBase(SessionState session)
        {
            Session = session;
            Dock = DockStyle.Fill;
            BackColor = Theme.Fundo;
            Font = Theme.Corpo;
            ForeColor = Theme.Texto;
            AutoScroll = true;
        }

        protected SessionState Session { get; }

        internal abstract string Title { get; }

        /// <summary>Texto do botão de avanço. A tela 7 encerra em vez de avançar.</summary>
        internal virtual string AdvanceText => Strings.BotaoAvancar;

        internal virtual bool CanGoBack => true;

        /// <summary>Se o avanço está liberado agora. Reavaliado a cada <see cref="StateChanged"/>.</summary>
        internal virtual bool CanAdvance => true;

        /// <summary>
        /// Motivo de o avanço estar bloqueado, para mostrar ao técnico em vez de deixá-lo
        /// clicando num botão desabilitado sem entender por quê.
        /// </summary>
        internal virtual string BlockedReason => null;

        internal virtual void OnEnter() { }

        /// <summary>Chamado antes de sair. Devolver false cancela a navegação.</summary>
        internal virtual bool TryLeave() => true;

        /// <summary>A tela mudou de estado e o rodapé precisa ser reavaliado.</summary>
        internal event EventHandler StateChanged;

        /// <summary>A tela quer avançar por conta própria, sem clique no rodapé.</summary>
        internal event EventHandler AdvanceRequested;

        protected void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

        protected void RaiseAdvanceRequested() => AdvanceRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// Empilha um controle com <see cref="DockStyle.Top"/> na ordem de leitura.
        ///
        /// O layout de Dock percorre a coleção do índice mais ALTO para o zero, então sem
        /// isto a tela sai invertida — o último controle inserido apareceria em cima.
        /// Jogando cada novo controle para o índice 0, a ordem de inserção passa a ser a
        /// ordem visual de cima para baixo, que é como se lê o código.
        ///
        /// Está aqui, e não repetido em cada tela, porque errar isso não gera erro de
        /// compilação nem exceção: gera uma tela de cabeça para baixo.
        /// </summary>
        protected static void Stack(Control parent, Control child)
        {
            parent.Controls.Add(child);
            parent.Controls.SetChildIndex(child, 0);
        }
    }
}
