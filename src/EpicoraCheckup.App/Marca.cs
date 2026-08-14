using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace EpicoraCheckup.App
{
    /// <summary>
    /// Os ativos da identidade visual da Epicora: a família tipográfica e o logotipo.
    ///
    /// Tudo viaja embutido no executável, pelo mesmo motivo da matriz de regras (ADR-013):
    /// um arquivo ao lado do .exe some na primeira vez que alguém copia só o executável, e
    /// o técnico não tem como perceber que faltou.
    ///
    /// <b>Nada aqui pode derrubar a ferramenta.</b> Fonte que não carrega, recurso que não
    /// abre, GDI que recusa — tudo cai no fallback e registra o motivo em
    /// <see cref="Falha"/>, que a tela 1 despeja no log. Um diagnóstico que não roda porque
    /// a fonte da marca falhou seria trocar o produto pela embalagem.
    /// </summary>
    internal static class Marca
    {
        private const string RecursoRegular = "marca/Alexandria-Regular.ttf";
        private const string RecursoDestaque = "marca/Alexandria-SemiBold.ttf";
        private const string RecursoLogo = "marca/logo-branco.png";
        private const string RecursoIcone = "marca/epicora.ico";

        /// <summary>
        /// Fallback tipográfico. É o que o manual de marca manda usar quando a Alexandria
        /// não é possível — o manual cita PowerPoint, Word e e-mail, e a lógica é a mesma
        /// aqui: máquina de cliente onde o registro da fonte falhou.
        /// </summary>
        private const string FonteDeFallback = "Arial";

        /// <summary>
        /// A coleção precisa sobreviver ao processo inteiro. Se coletada, os <see cref="Font"/>
        /// criados a partir dela passam a desenhar caixa vazia em vez de texto.
        /// </summary>
        private static readonly PrivateFontCollection Colecao = new PrivateFontCollection();

        private static readonly Image LogoBranco;

        /// <summary>Isotipo roxo, para a barra de título e o alt-tab. Null se não carregar.</summary>
        internal static readonly Icon Icone;

        internal static readonly FontFamily FamiliaCorrida;
        internal static readonly FontFamily FamiliaDestaque;

        /// <summary>Motivo da degradação, ou null quando tudo carregou. Vai para o log.</summary>
        internal static readonly string Falha;

        static Marca()
        {
            string falha = null;

            try
            {
                FamiliaCorrida = Registrar(RecursoRegular, "Alexandria");
                FamiliaDestaque = Registrar(RecursoDestaque, "Alexandria SemiBold");
            }
            catch (Exception ex)
            {
                falha = "fonte da marca não carregou, usando " + FonteDeFallback + ": " + ex.Message;
                FamiliaCorrida = null;
                FamiliaDestaque = null;
            }

            try
            {
                LogoBranco = Image.FromStream(new MemoryStream(LerRecurso(RecursoLogo)));
            }
            catch (Exception ex)
            {
                falha = Somar(falha, "logotipo não carregou: " + ex.Message);
                LogoBranco = null;
            }

            try
            {
                Icone = new Icon(new MemoryStream(LerRecurso(RecursoIcone)));
            }
            catch (Exception ex)
            {
                falha = Somar(falha, "ícone não carregou: " + ex.Message);
                Icone = null;
            }

            Falha = falha;
        }

        private static string Somar(string acumulado, string novo)
        {
            return string.IsNullOrEmpty(acumulado) ? novo : acumulado + " · " + novo;
        }

        // ------------------------------------------------------------ tipografia

        /// <summary>
        /// Fonte de exibição da marca, no tamanho pedido.
        ///
        /// Sempre <see cref="FontStyle.Regular"/>, e os dois pesos são famílias SEPARADAS
        /// ("Alexandria" e "Alexandria SemiBold"). Pedir <see cref="FontStyle.Bold"/> a uma
        /// família privada que não tem esse corte lança <see cref="ArgumentException"/> em
        /// vez de sintetizar — e o corte que o manual pede é o desenhado, não o engrossado
        /// pelo GDI+.
        /// </summary>
        internal static Font Fonte(float tamanho, bool destaque)
        {
            var familia = destaque ? FamiliaDestaque : FamiliaCorrida;

            if (familia != null)
            {
                try
                {
                    return new Font(familia, tamanho, FontStyle.Regular, GraphicsUnit.Point);
                }
                catch (ArgumentException)
                {
                    // Família registrada mas sem o corte regular. Não deveria acontecer com
                    // os arquivos deste repositório, mas cair aqui é melhor que não abrir.
                }
            }

            return new Font(FonteDeFallback, tamanho, destaque ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
        }

        private static FontFamily Registrar(string recurso, string familiaEsperada)
        {
            var bytes = LerRecurso(recurso);

            // Deliberadamente NUNCA liberado: AddMemoryFont e AddFontMemResourceEx guardam o
            // ponteiro, não uma cópia. Liberar depois de carregar é o erro clássico daqui —
            // funciona no desenvolvimento e desenha lixo na máquina do cliente.
            var ponteiro = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, ponteiro, bytes.Length);

            // Os DOIS registros são necessários, e é a parte não óbvia desta classe.
            //
            // AddMemoryFont atende o GDI+, que é quem desenha Label e Button. TextRenderer,
            // que a tela 3 usa para MEDIR a altura dos cartões, é GDI puro e não enxerga a
            // coleção privada — só o que passou por AddFontMemResourceEx. Registrar em um só
            // faz medida e desenho usarem fontes diferentes, e o cartão corta o texto do
            // cliente no fim da última linha.
            Colecao.AddMemoryFont(ponteiro, bytes.Length);

            uint instaladas = 0;
            if (AddFontMemResourceEx(ponteiro, (uint)bytes.Length, IntPtr.Zero, ref instaladas) == IntPtr.Zero)
                throw new InvalidOperationException("o GDI recusou " + familiaEsperada);

            var familia = Colecao.Families.FirstOrDefault(f => f.Name == familiaEsperada);
            if (familia == null)
                throw new InvalidOperationException("a família " + familiaEsperada + " não apareceu na coleção");

            return familia;
        }

        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern IntPtr AddFontMemResourceEx(IntPtr arquivo, uint tamanho, IntPtr reservado, ref uint instaladas);

        // ------------------------------------------------------------ logotipo

        /// <summary>Largura que o logotipo ocupa para caber na altura pedida, preservando a proporção.</summary>
        internal static int LarguraDoLogo(int altura)
        {
            if (LogoBranco == null) return 0;
            return (int)Math.Round(altura * (double)LogoBranco.Width / LogoBranco.Height);
        }

        /// <summary>
        /// Desenha o logotipo branco com o canto superior esquerdo em <paramref name="origem"/>.
        ///
        /// A origem é um PNG de 1076 px de largura, e não um ícone do tamanho final: a janela
        /// é PerMonitorV2 (app.manifest), então em tela de 150% o destino é 1,5× maior em
        /// pixels reais. Reduzir de um original grande é nítido; ampliar de um pequeno é o
        /// borrão que o manifest existe para evitar.
        /// </summary>
        internal static void Desenhar(Graphics g, Point origem, int altura)
        {
            if (LogoBranco == null) return;

            var modoAnterior = g.InterpolationMode;
            var deslocamentoAnterior = g.PixelOffsetMode;

            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            g.DrawImage(LogoBranco, new Rectangle(origem.X, origem.Y, LarguraDoLogo(altura), altura));

            g.InterpolationMode = modoAnterior;
            g.PixelOffsetMode = deslocamentoAnterior;
        }

        // ------------------------------------------------------------ recursos

        private static byte[] LerRecurso(string nome)
        {
            using (var fluxo = Assembly.GetExecutingAssembly().GetManifestResourceStream(nome))
            {
                if (fluxo == null)
                    throw new FileNotFoundException("recurso embutido ausente: " + nome);

                var bytes = new byte[fluxo.Length];
                var lidos = 0;
                while (lidos < bytes.Length)
                {
                    var passo = fluxo.Read(bytes, lidos, bytes.Length - lidos);
                    if (passo == 0) break;
                    lidos += passo;
                }

                return bytes;
            }
        }
    }
}
