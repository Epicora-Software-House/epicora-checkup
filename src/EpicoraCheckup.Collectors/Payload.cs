using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Collectors
{
    /// <summary>
    /// Montagem do payload de cada coletor no formato do schema 1.0.
    ///
    /// Existe por causa de duas armadilhas que já morderam o protótipo em campo, e que
    /// nenhum compilador pega:
    ///
    ///  1. **Lista de um item vira objeto solto.** No PowerShell é a pipeline desenrolando o
    ///     array; aqui seria alguém escrevendo o primeiro item direto. O schema exige array,
    ///     e o consolidador quebra ao encontrar objeto onde esperava lista.
    ///  2. **Vazio vira <c>null</c> ou <c>[]</c>, e os dois significam coisas diferentes.**
    ///     <see cref="ArrayOrNull"/> para "não havia dado"; <see cref="Array"/> para "havia
    ///     dado e a resposta é: nenhum".
    /// </summary>
    internal static class Payload
    {
        /// <summary>
        /// Passa o payload a limpo antes de ele sair do coletor: todo valor nulo vira token
        /// nulo DE VERDADE.
        ///
        /// **Por que isto existe, e por que não é cosmético.** Atribuir um <c>string</c> nulo a
        /// um <see cref="JObject"/> não produz um token nulo: produz um token de tipo
        /// <c>String</c> com conteúdo nulo. Serializado ele vira <c>null</c> e ninguém percebe,
        /// mas em memória o motor de regras — que decide disponibilidade por
        /// <c>Type == JTokenType.Null</c> — passa a enxergar o campo como PRESENTE.
        ///
        /// O efeito é exatamente a regra 1 de contribuição violada: uma falha de coleta deixa
        /// de resolver <c>Indeterminate</c> e vira achado avaliado, possivelmente NonCompliant,
        /// numa máquina sobre a qual não se mediu nada. Vale para campo de texto, que é a
        /// maioria dos campos do schema.
        ///
        /// A limpeza é feita num ponto só, e não em cada atribuição, porque a atribuição
        /// silenciosamente errada continuaria sendo a mais natural de escrever.
        /// </summary>
        public static JObject Sanitized(JObject data)
        {
            if (data == null) return null;

            var falsosNulos = data
                .Descendants()
                .OfType<JValue>()
                .Where(value => value.Value == null && value.Type != JTokenType.Null)
                .ToList();

            foreach (var value in falsosNulos)
                value.Replace(JValue.CreateNull());

            return data;
        }

        /// <summary>Array, ou <c>null</c> quando não há item. Espelha <c>ArrOrNull</c> do protótipo.</summary>
        public static JToken ArrayOrNull(IEnumerable<JToken> items)
        {
            var array = Array(items);
            return array.Count == 0 ? (JToken)JValue.CreateNull() : array;
        }

        /// <summary>Array sempre, mesmo vazio. Espelha <c>ArrOrEmpty</c> do protótipo.</summary>
        public static JArray Array(IEnumerable<JToken> items)
        {
            var array = new JArray();
            if (items == null) return array;

            foreach (var item in items)
                if (item != null) array.Add(item);

            return array;
        }

        public static JToken TextsOrNull(IEnumerable<string> items)
        {
            var array = Texts(items);
            return array.Count == 0 ? (JToken)JValue.CreateNull() : array;
        }

        public static JArray Texts(IEnumerable<string> items)
        {
            var array = new JArray();
            if (items == null) return array;

            foreach (var item in items)
                if (item != null) array.Add(new JValue(item));

            return array;
        }

        public static JArray Numbers(IEnumerable<int> items)
        {
            var array = new JArray();
            if (items == null) return array;

            foreach (var item in items)
                array.Add(new JValue(item));

            return array;
        }

        /// <summary>Data no formato <c>yyyy-MM-dd</c> do schema. Nulo continua nulo.</summary>
        public static JToken Date(DateTimeOffset? moment)
        {
            return moment.HasValue
                ? new JValue(moment.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                : (JToken)JValue.CreateNull();
        }

        /// <summary>
        /// Instante ISO 8601 **com offset de fuso**, que o schema exige. Hora local sem
        /// offset é ambígua no consolidador, que junta máquinas de fusos diferentes.
        /// </summary>
        public static JToken Moment(DateTimeOffset? moment)
        {
            return moment.HasValue
                ? new JValue(moment.Value.ToString("o", CultureInfo.InvariantCulture))
                : (JToken)JValue.CreateNull();
        }

        public static JToken Round(double? value, int digits)
        {
            return value.HasValue
                ? new JValue(Math.Round(value.Value, digits))
                : (JToken)JValue.CreateNull();
        }

        /// <summary>
        /// Consulta um mapa de código → nome. **Código fora do mapa vira <c>null</c>, nunca um
        /// palpite** — os mapas do projeto são parciais de propósito (doc 02 §4.1).
        /// </summary>
        public static string Lookup(IDictionary<int, string> map, int? code)
        {
            string name;
            return code.HasValue && map.TryGetValue(code.Value, out name) ? name : null;
        }
    }
}
