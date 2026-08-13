using System.Drawing;
using EpicoraCheckup.Core.Model;
using EpicoraCheckup.Core.Orchestration;

namespace EpicoraCheckup.App
{
    /// <summary>
    /// Cores, fontes e medidas, num só lugar.
    ///
    /// Existe pelo mesmo motivo de <see cref="Strings"/>: aparência espalhada por dezenas
    /// de construtores de controle é impossível de revisar. E porque a tela 3 é o
    /// entregável que o cliente olha — semáforo com cor inconsistente entre telas é o tipo
    /// de detalhe que faz um relatório parecer amador.
    ///
    /// As cores de severidade e de faixa NÃO são as saturadas puras: em projetor de sala de
    /// reunião e em tela com escala de 150%, vermelho puro sobre branco vibra e cansa. São
    /// tons escurecidos o suficiente para manter contraste de texto legível.
    /// </summary>
    internal static class Theme
    {
        internal static readonly Font Titulo = new Font("Segoe UI", 16f, FontStyle.Regular);
        internal static readonly Font Subtitulo = new Font("Segoe UI", 10f, FontStyle.Regular);
        internal static readonly Font Corpo = new Font("Segoe UI", 9.75f, FontStyle.Regular);
        internal static readonly Font CorpoNegrito = new Font("Segoe UI", 9.75f, FontStyle.Bold);
        internal static readonly Font Rotulo = new Font("Segoe UI", 9f, FontStyle.Regular);
        internal static readonly Font ScoreGrande = new Font("Segoe UI", 40f, FontStyle.Bold);
        internal static readonly Font Monoespacada = new Font("Consolas", 9.5f, FontStyle.Regular);

        internal static readonly Color Fundo = Color.FromArgb(250, 250, 250);
        internal static readonly Color FundoCartao = Color.White;
        internal static readonly Color Borda = Color.FromArgb(222, 222, 222);
        internal static readonly Color Texto = Color.FromArgb(32, 32, 32);
        internal static readonly Color TextoSecundario = Color.FromArgb(105, 105, 105);

        internal static readonly Color Critico = Color.FromArgb(168, 28, 28);
        internal static readonly Color Alto = Color.FromArgb(196, 74, 22);
        internal static readonly Color Medio = Color.FromArgb(158, 118, 8);
        internal static readonly Color Baixo = Color.FromArgb(92, 92, 92);
        internal static readonly Color Info = Color.FromArgb(120, 120, 120);

        internal static readonly Color BandaVerde = Color.FromArgb(28, 122, 62);
        internal static readonly Color BandaAmarela = Color.FromArgb(158, 118, 8);
        internal static readonly Color BandaVermelha = Color.FromArgb(168, 28, 28);

        internal static readonly Color Indeterminado = Color.FromArgb(96, 106, 128);

        /// <summary>Faixa do modo demonstração. Precisa ser impossível de confundir com produção.</summary>
        internal static readonly Color DemoFundo = Color.FromArgb(120, 32, 128);
        internal static readonly Color DemoTexto = Color.White;

        /// <summary>Link clicável. O único da ferramenta é o de download da versão nova.</summary>
        internal static readonly Color Link = Color.FromArgb(0, 90, 158);

        internal const int Margem = 24;
        internal const int EspacoEntreCampos = 10;
        internal const int LarguraRotulo = 260;

        internal static Color CorDaSeveridade(Severity severity)
        {
            switch (severity)
            {
                case Severity.Critical: return Critico;
                case Severity.High: return Alto;
                case Severity.Medium: return Medio;
                case Severity.Low: return Baixo;
                default: return Info;
            }
        }

        internal static string NomeDaSeveridade(Severity severity)
        {
            switch (severity)
            {
                case Severity.Critical: return Strings.SeveridadeCritico;
                case Severity.High: return Strings.SeveridadeAlto;
                case Severity.Medium: return Strings.SeveridadeMedio;
                case Severity.Low: return Strings.SeveridadeBaixo;
                default: return Strings.SeveridadeInfo;
            }
        }

        internal static Color CorDaFaixa(ScoreBand band)
        {
            switch (band)
            {
                case ScoreBand.Green: return BandaVerde;
                case ScoreBand.Yellow: return BandaAmarela;
                default: return BandaVermelha;
            }
        }

        internal static string NomeDaFaixa(ScoreBand band)
        {
            switch (band)
            {
                case ScoreBand.Green: return Strings.FaixaVerde;
                case ScoreBand.Yellow: return Strings.FaixaAmarela;
                default: return Strings.FaixaVermelha;
            }
        }

        internal static string NomeDoVeredito(Verdict verdict)
        {
            switch (verdict)
            {
                case Verdict.Keep: return Strings.VereditoManter;
                case Verdict.Upgrade: return Strings.VereditoUpgrade;
                default: return Strings.VereditoSubstituir;
            }
        }

        internal static string NomeDaFase(CollectorPhase phase)
        {
            switch (phase)
            {
                case CollectorPhase.Pending: return Strings.EtapaPendente;
                case CollectorPhase.Running: return Strings.EtapaExecutando;
                case CollectorPhase.Completed: return Strings.EtapaConcluido;
                case CollectorPhase.Skipped: return Strings.EtapaIgnorado;
                default: return Strings.EtapaFalhou;
            }
        }

        internal static Color CorDaFase(CollectorPhase phase)
        {
            switch (phase)
            {
                case CollectorPhase.Completed: return BandaVerde;
                case CollectorPhase.Running: return Color.FromArgb(28, 86, 158);
                case CollectorPhase.Skipped: return Indeterminado;
                case CollectorPhase.Failed: return Alto;
                default: return TextoSecundario;
            }
        }
    }
}
