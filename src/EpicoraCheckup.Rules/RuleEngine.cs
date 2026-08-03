using System;
using System.Collections.Generic;
using System.Linq;
using EpicoraCheckup.Core.Model;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Rules
{
    /// <summary>
    /// Resultado de uma avaliação, mais os avisos de diagnóstico.
    ///
    /// Os avisos não fazem parte do JSON de saída nem dos golden files: são para quem
    /// mantém a matriz, e apontam regra que compara um caminho nulo que não está em
    /// requires — às vezes intencional, às vezes bug de regra.
    /// </summary>
    public sealed class RuleEvaluation
    {
        public EvaluationResult Result { get; internal set; }

        public IReadOnlyList<string> Warnings { get; internal set; }
    }

    /// <summary>
    /// Motor de regras. Avalia a matriz declarativa contra um documento de coleta e
    /// produz achados mais score.
    ///
    /// Contrato de aceite: tests/expected/*.matriz-completa.json, gerados pelo motor de
    /// referência em Node. Quando este motor passa nos três, o de referência é
    /// aposentado — ele é instrumento, não segundo sistema.
    /// </summary>
    public sealed class RuleEngine
    {
        private readonly IReadOnlyList<Rule> _rules;

        public RuleEngine(IReadOnlyList<Rule> rules)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        /// <param name="includePending">
        /// Inclui as regras desabilitadas. Serve para conferir a matriz inteira contra
        /// uma máquina conhecida antes de habilitar regra. A ferramenta em produção
        /// roda sempre com false — regra sem clientText aprovado não vai ao cliente.
        /// </param>
        public RuleEvaluation Evaluate(JObject document, bool includePending = false)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var reader = new DocumentReader(document);
            var warnings = new List<string>();
            var findings = new List<Finding>();

            // Ordem de carga preservada: VerdictDrivenBy depende dela.
            foreach (var rule in _rules)
            {
                if (!rule.Enabled && !includePending) continue;
                findings.Add(EvaluateRule(rule, reader, warnings));
            }

            var score = BuildScore(findings);

            // A ordenação para exibição vem DEPOIS do score e do veredito, de propósito.
            SortForPresentation(findings);

            return new RuleEvaluation
            {
                Result = new EvaluationResult { Findings = findings, Score = score },
                Warnings = warnings
            };
        }

        private Finding EvaluateRule(Rule rule, DocumentReader reader, List<string> warnings)
        {
            var finding = new Finding
            {
                RuleId = rule.Id,
                RuleVersion = rule.Version,
                Severity = rule.Severity,
                IndeterminateReason = null,
                Weight = rule.Weight,
                Title = rule.Title,
                ClientText = rule.ClientText,
                RecommendedAction = rule.RecommendedAction,
                Evidence = null,
                LinkedOptimizations = rule.LinkedOptimizations ?? new List<string>(),
                MarkedFalsePositive = false,
                FalsePositiveJustification = null
            };

            // 1. requires — caminho ausente, ou coletor que não concluiu, resolve
            //    Indeterminate. NUNCA NonCompliant. Falha de coleta não é achado negativo.
            var blocked = FirstBlockingRequirement(rule, reader);

            if (blocked != null)
            {
                finding.State = RuleState.Indeterminate;
                finding.IndeterminateReason = blocked;
            }
            else if (rule.IndeterminateWhen != null && Evaluate(rule.IndeterminateWhen, rule, reader, warnings))
            {
                // 2. indeterminateWhen — o guard que impede um "Unknown" de enum de passar
                //    por um notEquals e virar Compliant em silêncio.
                finding.State = RuleState.Indeterminate;
                finding.IndeterminateReason = IndeterminateReasonFor(rule);
            }
            else
            {
                // 3. condition — verdadeira é NonCompliant, falsa é Compliant.
                finding.State = Evaluate(rule.Condition, rule, reader, warnings)
                    ? RuleState.NonCompliant
                    : RuleState.Compliant;
            }

            if (finding.State == RuleState.NonCompliant)
                finding.Evidence = CollectEvidence(rule, reader);

            return finding;
        }

        private static string FirstBlockingRequirement(Rule rule, DocumentReader reader)
        {
            foreach (var path in rule.Requires ?? Enumerable.Empty<string>())
            {
                var result = reader.Read(path);
                if (result.IsUnavailable) return result.UnavailableReason;
                if (result.IsNullish) return $"dado não disponível: {path}";
            }

            return null;
        }

        /// <summary>
        /// Motivo mostrado no bloco "não foi possível verificar". Usa a primeira frase da
        /// validationNote da regra — o corte no primeiro ponto é o comportamento do motor
        /// de referência e faz parte dos golden files.
        /// </summary>
        private static string IndeterminateReasonFor(Rule rule)
        {
            const string prefix = "condição de indeterminação atendida";

            if (string.IsNullOrEmpty(rule.ValidationNote)) return prefix;

            var firstSentence = rule.ValidationNote.Split('.')[0];
            return $"{prefix} — {firstSentence}";
        }

        private static IDictionary<string, object> CollectEvidence(Rule rule, DocumentReader reader)
        {
            var evidence = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (var path in rule.EvidenceFields ?? Enumerable.Empty<string>())
            {
                var result = reader.Read(path);
                if (result.IsUnavailable || result.IsNullish) continue;

                evidence[path] = result.Value;
            }

            // Sem evidência disponível o campo é nulo, não um objeto vazio.
            return evidence.Count > 0 ? evidence : null;
        }

        private static bool Evaluate(Condition condition, Rule rule, DocumentReader reader, List<string> warnings)
        {
            if (condition == null) return false;

            if (condition.AllOf != null) return condition.AllOf.All(c => Evaluate(c, rule, reader, warnings));
            if (condition.AnyOf != null) return condition.AnyOf.Any(c => Evaluate(c, rule, reader, warnings));
            if (condition.Not != null) return !Evaluate(condition.Not, rule, reader, warnings);

            // Folha sem operador é matriz malformada. Falhar aqui, nomeando a regra, em
            // vez de deixar estourar mais adiante com exceção que não diz qual regra é.
            if (string.IsNullOrEmpty(condition.Operator))
                throw new InvalidOperationException($"{rule.Id}: condição sem \"operator\" no caminho \"{condition.Path}\"");

            var result = reader.Read(condition.Path);
            var value = result.Value;

            // Comparação sobre valor ausente que NÃO está em requires resolve em silêncio.
            // Às vezes é intencional, às vezes é bug de regra — então avisa.
            // O texto replica o do motor de referência para os dois serem comparáveis
            // durante a transição; ele diz "resolveu falso", o que não é exato para
            // notEquals. É diagnóstico, não contrato.
            if (ReadResult.IsNullish_(value)
                && !OperatorEvaluator.NullAware.Contains(condition.Operator)
                && !(rule.Requires ?? Enumerable.Empty<string>()).Contains(condition.Path, StringComparer.Ordinal))
            {
                warnings.Add($"{rule.Id}: \"{condition.Path}\" está nulo e não consta em requires — a comparação \"{condition.Operator}\" resolveu falso");
            }

            return OperatorEvaluator.Apply(condition.Operator, value, condition.Value);
        }

        private Score BuildScore(IEnumerable<Finding> findings)
        {
            var nonCompliant = findings.Where(f => f.State == RuleState.NonCompliant).ToList();

            var value = Math.Max(0, 100 - nonCompliant.Sum(f => f.Weight));
            var band = value >= 80 ? ScoreBand.Green : value >= 50 ? ScoreBand.Yellow : ScoreBand.Red;

            var influenceById = _rules.ToDictionary(r => r.Id, r => r.VerdictInfluence, StringComparer.Ordinal);

            var replaceDrivers = nonCompliant.Where(f => InfluenceOf(influenceById, f) == "Replace").ToList();
            var upgradeDrivers = nonCompliant.Where(f => InfluenceOf(influenceById, f) == "Upgrade").ToList();

            var verdict = replaceDrivers.Count > 0 ? Verdict.Replace
                : upgradeDrivers.Count > 0 ? Verdict.Upgrade
                : Verdict.Keep;

            var drivers = replaceDrivers.Count > 0 ? replaceDrivers : upgradeDrivers;

            return new Score
            {
                Value = value,
                Band = band,
                Verdict = verdict,
                VerdictDrivenBy = drivers.Select(f => f.RuleId).ToList()
            };
        }

        private static string InfluenceOf(IDictionary<string, string> influenceById, Finding finding)
        {
            string influence;
            return influenceById.TryGetValue(finding.RuleId, out influence) ? influence : null;
        }

        /// <summary>
        /// Severidade primeiro, id como desempate.
        ///
        /// O desempate usa comparação ORDINAL. O motor de referência usa
        /// String.localeCompare, e para os ids da matriz — prefixo em maiúsculas, hífen,
        /// três dígitos — os dois dão a mesma ordem. Se algum dia entrar id com forma
        /// diferente, os golden files pegam a divergência.
        /// </summary>
        private static void SortForPresentation(List<Finding> findings)
        {
            findings.Sort((a, b) =>
            {
                var bySeverity = ((int)a.Severity).CompareTo((int)b.Severity);
                return bySeverity != 0 ? bySeverity : string.CompareOrdinal(a.RuleId, b.RuleId);
            });
        }
    }
}
