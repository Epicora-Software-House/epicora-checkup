using System.Collections.Generic;

namespace EpicoraCheckup.Core.Model
{
    /// <summary>
    /// Score, faixa e veredito da máquina — o conteúdo do topo da tela 3.
    ///
    /// Calibração: o modelo atual é soma linear de pesos com piso em zero, e
    /// tests/README.md já registra que ele satura — a fixture desenhada para ser
    /// Amarelo sai Vermelho sob a matriz completa. Os pesos precisam ser recalibrados
    /// depois das dez primeiras máquinas reais. Isso é achado medido, não bug deste
    /// tipo, e está registrado para não se perder.
    /// </summary>
    public sealed class Score
    {
        /// <summary>0 a 100. Nunca negativo.</summary>
        public int Value { get; set; }

        public ScoreBand Band { get; set; }

        public Verdict Verdict { get; set; }

        /// <summary>
        /// Ids das regras que determinaram o veredito. Existe para o relatório poder
        /// dizer <em>por que</em> a máquina é "Substituir" em vez de só afirmar que é —
        /// e para o técnico poder contestar a regra, não o número.
        /// </summary>
        public IList<string> VerdictDrivenBy { get; set; }
    }

    /// <summary>Resultado completo de uma avaliação: achados mais score.</summary>
    public sealed class EvaluationResult
    {
        public IList<Finding> Findings { get; set; }

        public Score Score { get; set; }
    }
}
