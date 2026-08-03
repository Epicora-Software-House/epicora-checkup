using System;
using System.Collections.Generic;
using EpicoraCheckup.Core.Contracts;
using EpicoraCheckup.Core.Model;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Rules
{
    /// <summary>
    /// Monta o documento que o motor de regras avalia, a partir dos resultados dos coletores.
    ///
    /// É o contrato de ENTRADA do motor, e por isso vive junto dele. Só produz o que o
    /// <see cref="DocumentReader"/> consome — a lista de coletores. Os blocos de
    /// identificação, dados manuais, achados e score são do documento COMPLETO do schema 1.0,
    /// que é responsabilidade de Reporting.
    ///
    /// Existir separado importa por um motivo prático: garante que a avaliação rode sobre os
    /// resultados dos coletores, e não sobre o JSON de onde eles porventura vieram. No modo
    /// demonstração as duas coisas seriam parecidas, e usar o atalho faria a demonstração
    /// exercitar um caminho que produção não usa.
    /// </summary>
    public static class CollectionDocumentBuilder
    {
        public static JObject FromResults(IEnumerable<CollectorResult> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));

            var collectors = new JArray();

            foreach (var result in results)
                collectors.Add(ToJson(result));

            return new JObject { ["collectors"] = collectors };
        }

        private static JObject ToJson(CollectorResult result)
        {
            return new JObject
            {
                ["id"] = result.Id,
                ["displayName"] = result.DisplayName,
                ["status"] = result.Status.ToString(),
                ["skipReason"] = result.SkipReason == null ? JValue.CreateNull() : new JValue(result.SkipReason),
                ["requiresElevation"] = result.RequiresElevation,
                ["durationMs"] = result.DurationMs,
                ["timedOut"] = result.TimedOut,
                ["summary"] = result.Summary == null ? JValue.CreateNull() : new JValue(result.Summary),
                ["errors"] = ErrorsToJson(result.Errors),

                // O payload já é JToken quando vem de fixture, e objeto CLR quando vem de
                // coletor real. FromObject trata os dois; JToken passa direto.
                ["data"] = result.Data == null ? JValue.CreateNull() : (result.Data as JToken ?? JToken.FromObject(result.Data))
            };
        }

        private static JArray ErrorsToJson(IList<CollectorError> errors)
        {
            var array = new JArray();
            if (errors == null) return array;

            foreach (var error in errors)
            {
                array.Add(new JObject
                {
                    ["source"] = error.Source,
                    ["message"] = error.Message
                    // detail fica fora: é para o log e o pacote interno, não para a avaliação.
                });
            }

            return array;
        }
    }
}
