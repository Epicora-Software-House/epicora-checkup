using System.Drawing;
using System.Windows.Forms;

namespace EpicoraCheckup.App.Controls
{
    /// <summary>
    /// Cria rótulos de texto corrido com altura já medida.
    ///
    /// **Por que não usar AutoSize.** Um <see cref="Label"/> com <c>AutoSize</c> só conhece a
    /// própria altura depois de entrar num layout, e as telas 3 e 7 precisam da altura ANTES —
    /// é ela que define onde começa o próximo elemento e qual a altura do cartão. Ler
    /// <c>Height</c> antes disso devolve o valor padrão, e o resultado é cartão curto com
    /// texto cortado, ou cartões sobrepostos.
    ///
    /// <see cref="TextRenderer.MeasureText(string, Font, Size, TextFormatFlags)"/> mede sem
    /// depender de layout, e é o mesmo motor de desenho que os rótulos usam — a aplicação
    /// chama <c>SetCompatibleTextRenderingDefault(false)</c>, então medir por GDI+ daria
    /// número de outro renderizador.
    /// </summary>
    internal static class TextBlock
    {
        internal static Label Wrapped(string text, Font font, Color color, int width, int left, int top)
        {
            var conteudo = text ?? string.Empty;

            var medida = TextRenderer.MeasureText(
                conteudo,
                font,
                new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

            return new Label
            {
                Left = left,
                Top = top,
                AutoSize = false,
                Size = new Size(width, medida.Height),
                Font = font,
                ForeColor = color,
                Text = conteudo
            };
        }
    }
}
