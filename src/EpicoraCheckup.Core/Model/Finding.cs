using System.Collections.Generic;

namespace EpicoraCheckup.Core.Model
{
    /// <summary>
    /// Resultado da avaliação de uma regra contra um documento de coleta.
    ///
    /// A ordem das propriedades espelha o bloco "findings" do schema 1.0 e a saída do
    /// motor de referência em tools/evaluate-rules.mjs. Os golden files em
    /// tests/expected/ são o contrato: este tipo serializado tem que dar neles.
    /// </summary>
    public sealed class Finding
    {
        public string RuleId { get; set; }

        /// <summary>
        /// Versão da regra que produziu este achado. Vai no JSON para que um relatório
        /// contestado meses depois possa ser reproduzido com a regra da época.
        /// </summary>
        public int RuleVersion { get; set; }

        public Severity Severity { get; set; }

        public RuleState State { get; set; }

        /// <summary>
        /// Por que ficou indeterminado, em linguagem de relatório. Nulo quando
        /// <see cref="State"/> não é <see cref="RuleState.Indeterminate"/>.
        /// É o texto que aparece no bloco "não foi possível verificar".
        /// </summary>
        public string IndeterminateReason { get; set; }

        /// <summary>Peso subtraído do score quando <see cref="RuleState.NonCompliant"/>.</summary>
        public int Weight { get; set; }

        public string Title { get; set; }

        /// <summary>
        /// Texto de cliente, vindo de rules/*.json. Nulo enquanto o comercial não
        /// aprovar — e regra sem clientText aprovado não entra em release.
        /// </summary>
        public string ClientText { get; set; }

        public string RecommendedAction { get; set; }

        /// <summary>
        /// Mapa caminho → valor com a evidência que sustenta o achado. Só é preenchido
        /// quando <see cref="RuleState.NonCompliant"/>; nulo quando não há evidência
        /// disponível. As chaves são caminhos pontilhados do documento de coleta, e
        /// NÃO devem ser transformadas na serialização.
        ///
        /// Os valores são deliberadamente <c>object</c>: Core não referencia biblioteca
        /// de JSON, então quem avalia deposita aqui o token que leu.
        /// </summary>
        public IDictionary<string, object> Evidence { get; set; }

        public IList<string> LinkedOptimizations { get; set; }

        /// <summary>
        /// Marcado pelo técnico na tela 3. Vai para o JSON e alimenta a melhoria das
        /// regras — é como um falso positivo de campo volta para a matriz.
        /// </summary>
        public bool MarkedFalsePositive { get; set; }

        public string FalsePositiveJustification { get; set; }
    }
}
