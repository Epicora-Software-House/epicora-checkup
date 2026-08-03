using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Rules
{
    /// <summary>
    /// Os 14 operadores da matriz (rules/README.md).
    ///
    /// A semântica é a do motor de referência em tools/evaluate-rules.mjs, que é JS, e
    /// os golden files em tests/expected/ foram gerados por ele. Onde JS e C# divergem
    /// naturalmente, o comentário diz qual comportamento foi replicado e por quê —
    /// divergência silenciosa aqui muda o resultado da matriz sem ninguém notar.
    /// </summary>
    internal static class OperatorEvaluator
    {
        /// <summary>
        /// Operadores que tratam ausência como entrada legítima. Só estes podem ser
        /// aplicados a um valor nulo sem indicar possível bug de regra.
        /// </summary>
        internal static readonly HashSet<string> NullAware = new HashSet<string>(StringComparer.Ordinal)
        {
            "isNull", "isNotNull", "isTrue", "isFalse", "isEmpty", "isNotEmpty"
        };

        internal static bool Apply(string op, object value, JToken expected)
        {
            switch (op)
            {
                case "isNull": return ReadResult.IsNullish_(value);
                case "isNotNull": return !ReadResult.IsNullish_(value);

                // Estritamente true/false: um valor nulo não é nenhum dos dois, e é isso
                // que faz uma leitura falhada resolver Indeterminate em vez de "conforme".
                case "isTrue": return IsBool(value, true);
                case "isFalse": return IsBool(value, false);

                // Só array. Campo ausente não é "vazio" — não afirmamos ausência de
                // agente de backup porque a leitura falhou.
                case "isEmpty": return AsArray(value)?.Count == 0;
                case "isNotEmpty": return AsArray(value)?.Count > 0;

                case "equals": return StrictEquals(value, expected);
                case "notEquals": return !StrictEquals(value, expected);

                // Em JS, `typeof v === 'number' && v < expected`. Valor não numérico
                // devolve falso em vez de lançar ou coagir.
                case "lessThan": return CompareNumbers(value, expected, (a, b) => a < b);
                case "greaterThan": return CompareNumbers(value, expected, (a, b) => a > b);

                case "contains": return Contains(value, expected);

                // Assimetria deliberada, copiada do motor de referência: para um valor
                // que não é array nem string, notContains devolve FALSO, não verdadeiro.
                // Ou seja, não afirma "não contém" sobre algo que não pôde ser lido.
                case "notContains":
                {
                    var array = AsArray(value);
                    if (array != null) return !ArrayIncludes(array, expected);

                    string text;
                    return TryAsString(value, out text) && !text.Contains(AsStringOrEmpty(expected));
                }

                case "inList": return ExpectedList(expected)?.Any(item => StrictEquals(value, item)) ?? false;
                case "notInList":
                {
                    var list = ExpectedList(expected);
                    return list != null && !list.Any(item => StrictEquals(value, item));
                }

                default:
                    throw new NotSupportedException($"operador desconhecido: {op}");
            }
        }

        private static bool Contains(object value, JToken expected)
        {
            var array = AsArray(value);
            if (array != null) return ArrayIncludes(array, expected);

            string text;
            return TryAsString(value, out text) && text.Contains(AsStringOrEmpty(expected));
        }

        private static bool ArrayIncludes(JArray array, JToken expected)
        {
            return array.Any(item => StrictEquals(item, expected));
        }

        private static IEnumerable<JToken> ExpectedList(JToken expected)
        {
            return expected as JArray;
        }

        private static bool IsBool(object value, bool wanted)
        {
            var token = value as JToken;
            return token != null && token.Type == JTokenType.Boolean && (bool)token == wanted;
        }

        private static JArray AsArray(object value)
        {
            return value as JArray;
        }

        private static bool TryAsString(object value, out string text)
        {
            text = null;
            var token = value as JToken;
            if (token == null || token.Type != JTokenType.String) return false;

            text = (string)token;
            return text != null;
        }

        private static string AsStringOrEmpty(JToken expected)
        {
            return expected == null ? string.Empty : expected.ToString();
        }

        private static bool CompareNumbers(object value, JToken expected, Func<double, double, bool> compare)
        {
            double left, right;
            if (!TryAsNumber(value, out left)) return false;
            if (!TryAsNumber(expected, out right)) return false;

            return compare(left, right);
        }

        private static bool TryAsNumber(object value, out double number)
        {
            number = 0;
            var token = value as JToken;
            if (token == null) return false;

            // Booleano não é número, e string numérica não é número — em JS, `typeof`
            // separa os três, e não há coerção aqui.
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) return false;

            number = (double)token;
            return true;
        }

        /// <summary>
        /// Equivalente ao <c>===</c> de JavaScript.
        ///
        /// <see cref="Missing"/> nunca é igual a nada, inclusive a null: campo ausente e
        /// campo nulo são estados diferentes. Objeto e array comparam por referência em
        /// JS, e como cada leitura devolve uma instância distinta, isso é sempre falso —
        /// replicado aqui em vez de comparar estrutura, que daria outro resultado.
        /// </summary>
        private static bool StrictEquals(object value, JToken expected)
        {
            if (ReferenceEquals(value, Missing.Instance)) return false;

            var token = value as JToken;
            if (token == null) return false;

            var left = token.Type;
            var right = expected == null ? JTokenType.Undefined : expected.Type;

            if (left == JTokenType.Null || left == JTokenType.Undefined)
                return right == JTokenType.Null;

            switch (left)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    if (right != JTokenType.Integer && right != JTokenType.Float) return false;
                    return (double)token == (double)expected;

                case JTokenType.String:
                    return right == JTokenType.String && string.Equals((string)token, (string)expected, StringComparison.Ordinal);

                case JTokenType.Boolean:
                    return right == JTokenType.Boolean && (bool)token == (bool)expected;

                default:
                    return false;
            }
        }
    }
}
