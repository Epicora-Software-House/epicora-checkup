using System.Collections.Generic;
using EpicoraCheckup.Core.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Rules
{
    /// <summary>
    /// Uma condição da matriz. É uma folha (<see cref="Path"/> + <see cref="Operator"/>)
    /// ou um nó de composição (<see cref="AllOf"/>, <see cref="AnyOf"/>, <see cref="Not"/>).
    ///
    /// O conjunto de operadores é deliberadamente pequeno (rules/README.md): regra que
    /// não cabe nele precisa de um campo derivado calculado no coletor, que é mais
    /// testável. É assim que memory.freeSlots e network.linkDowngraded existem.
    /// </summary>
    public sealed class Condition
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        /// <summary>Nome do operador. "operator" é palavra reservada em C#.</summary>
        [JsonProperty("operator")]
        public string Operator { get; set; }

        /// <summary>
        /// Valor esperado, cru. Fica como <see cref="JToken"/> porque pode ser número,
        /// texto, booleano ou lista (inList / notInList), e converter cedo perderia a
        /// distinção entre ausente e nulo.
        /// </summary>
        [JsonProperty("value")]
        public JToken Value { get; set; }

        [JsonProperty("allOf")]
        public IList<Condition> AllOf { get; set; }

        [JsonProperty("anyOf")]
        public IList<Condition> AnyOf { get; set; }

        [JsonProperty("not")]
        public Condition Not { get; set; }
    }

    /// <summary>
    /// Uma regra da matriz, como vive em rules/*.json.
    ///
    /// Regras nunca são deletadas: marcam-se <see cref="Enabled"/> como false, para que
    /// relatórios antigos permaneçam auditáveis e reprodutíveis.
    /// </summary>
    public sealed class Rule
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("version")]
        public int Version { get; set; }

        /// <summary>
        /// Uma regra só nasce habilitada quando tem fonte de confiança A e clientText
        /// aprovado pelo comercial. Hoje isso são 5 das 61 — as outras aguardam
        /// validação de campo, e o motivo de cada uma está em EnabledBlockedBy.
        /// </summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("enabledBlockedBy")]
        public IList<string> EnabledBlockedBy { get; set; }

        [JsonProperty("sourceConfidence")]
        public string SourceConfidence { get; set; }

        /// <summary>
        /// O que precisa ser confirmado em campo antes de habilitar. Também serve de
        /// texto de indeterminação: quando <see cref="IndeterminateWhen"/> dispara, a
        /// primeira frase daqui é o motivo mostrado no relatório.
        /// </summary>
        [JsonProperty("validationNote")]
        public string ValidationNote { get; set; }

        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("severity")]
        public Severity Severity { get; set; }

        [JsonProperty("weight")]
        public int Weight { get; set; }

        /// <summary>
        /// Caminhos obrigatórios. Se qualquer um resolver nulo, ou se o coletor de
        /// origem não estiver Completed, a regra resolve Indeterminate — nunca
        /// NonCompliant. É a regra número um do projeto.
        /// </summary>
        [JsonProperty("requires")]
        public IList<string> Requires { get; set; }

        /// <summary>
        /// Guard que impede um enum "Unknown" de passar em silêncio por um notEquals e
        /// virar Compliant. Sem ele, mediaType "Unknown" avaliado por notEquals "HDD"
        /// resolveria conforme — um falso negativo silencioso.
        /// </summary>
        [JsonProperty("indeterminateWhen")]
        public Condition IndeterminateWhen { get; set; }

        [JsonProperty("condition")]
        public Condition Condition { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("clientText")]
        public string ClientText { get; set; }

        [JsonProperty("recommendedAction")]
        public string RecommendedAction { get; set; }

        [JsonProperty("evidenceFields")]
        public IList<string> EvidenceFields { get; set; }

        [JsonProperty("linkedOptimizations")]
        public IList<string> LinkedOptimizations { get; set; }

        /// <summary>"Replace", "Upgrade" ou nulo. String, e não enum, para preservar o nulo.</summary>
        [JsonProperty("verdictInfluence")]
        public string VerdictInfluence { get; set; }
    }
}
