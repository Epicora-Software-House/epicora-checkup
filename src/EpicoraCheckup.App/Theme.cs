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
    ///
    /// <b>Onde a marca entra e onde não entra.</b> A identidade da Epicora manda no
    /// cromo — cabeçalho, ação principal, link, tipografia de exibição. O semáforo é
    /// SEMÂNTICO e não obedece a ela: ver <see cref="BandaVerde"/>.
    /// </summary>
    internal static class Theme
    {
        // ------------------------------------------------------------ paleta da marca
        //
        // Manual de marca da Epicora, seção "cores", mais os tons de apoio que os decks
        // comerciais já usam. Roxo é a cor-mãe: é o isotipo e é o que o cliente reconhece.

        internal static readonly Color Roxo = Color.FromArgb(0x61, 0x00, 0xFF);
        internal static readonly Color RoxoProfundo = Color.FromArgb(0x2A, 0x0A, 0x78);
        internal static readonly Color Lilas = Color.FromArgb(0xA9, 0x8B, 0xFF);

        /// <summary>Texto secundário sobre o roxo. O lilás do manual não tem contraste suficiente ali.</summary>
        internal static readonly Color SobreRoxoSuave = Color.FromArgb(0xD6, 0xC7, 0xFF);

        internal static readonly Color Ink = Color.FromArgb(0x08, 0x08, 0x0A);
        internal static readonly Color AmareloNeon = Color.FromArgb(0xDA, 0xFF, 0x19);

        // ------------------------------------------------------------ tipografia
        //
        // Exibição na Alexandria, corpo no Segoe UI, e a divisão é deliberada.
        //
        // O manual pede Alexandria no texto corrido, mas o corrido desta ferramenta é o
        // clientText da tela 3 — parágrafos longos, lidos por cima do ombro do técnico, em
        // 9,75pt. Nesse tamanho o Segoe UI foi desenhado para a tela do Windows e a
        // Alexandria não. Trocar custaria legibilidade justamente no entregável.
        //
        // O outro motivo é de risco: o corrido vive em painel de altura fixa e em cartão
        // medido com TextRenderer. Métrica diferente ali corta texto. Título e score não
        // têm esse problema, e são o que dá o reconhecimento da marca.

        internal static readonly Font Titulo = Marca.Fonte(15f, true);
        internal static readonly Font Subtitulo = Marca.Fonte(10f, false);
        internal static readonly Font ScoreGrande = Marca.Fonte(38f, true);

        internal static readonly Font Corpo = new Font("Segoe UI", 9.75f, FontStyle.Regular);
        internal static readonly Font CorpoNegrito = new Font("Segoe UI", 9.75f, FontStyle.Bold);
        internal static readonly Font Rotulo = new Font("Segoe UI", 9f, FontStyle.Regular);
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

        /// <summary>
        /// Faixas do score. <b>Não são as cores da marca, e é de propósito.</b>
        ///
        /// O verde-água #14FFB9 do manual é o indicador positivo da identidade, mas sobre
        /// branco ele tem contraste perto de 1,5:1 — some. Vale o mesmo para o amarelo neon.
        /// O semáforo é lido em projetor de sala de reunião e em tela de 150%, e ali a regra
        /// que manda é contraste, não paleta. A marca aparece no cromo ao redor.
        /// </summary>
        internal static readonly Color BandaVerde = Color.FromArgb(28, 122, 62);

        internal static readonly Color BandaAmarela = Color.FromArgb(158, 118, 8);
        internal static readonly Color BandaVermelha = Color.FromArgb(168, 28, 28);

        internal static readonly Color Indeterminado = Color.FromArgb(96, 106, 128);

        /// <summary>
        /// Faixa do modo demonstração. Precisa ser impossível de confundir com produção.
        ///
        /// Era roxa, e o roxo virou a cor do cabeçalho: a faixa que existe para gritar
        /// "isto não é real" passaria a ter exatamente a cor do cromo normal. É o amarelo
        /// neon da paleta sobre o preto dela — combinação de sinalização, e a única da
        /// marca que não colide com o semáforo nem com o cabeçalho.
        /// </summary>
        internal static readonly Color DemoFundo = AmareloNeon;

        internal static readonly Color DemoTexto = Ink;

        /// <summary>Link clicável. O único da ferramenta é o de download da versão nova.</summary>
        internal static readonly Color Link = Roxo;

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
