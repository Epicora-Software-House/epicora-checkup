using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using EpicoraCheckup.Collectors.Sources;
using EpicoraCheckup.Core.Contracts;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Collectors.Collectors
{
    /// <summary>
    /// Antivírus — o ponto mais delicado da ferramenta.
    ///
    /// **Não é marcado <c>RequiresElevation</c>, e isso foi MEDIDO EM CAMPO:** nem
    /// <c>root\SecurityCenter2</c> nem <c>MSFT_MpComputerStatus</c> exigem privilégio. O gate
    /// anterior descartava SEC-001/002/003 e SW-005 — a família mais valiosa do relatório — em
    /// toda visita em que o técnico não conseguisse elevar.
    /// </summary>
    public sealed class AntivirusCollector : CollectorBase
    {
        public override string Id
        {
            get { return "antivirus"; }
        }

        public override string DisplayName
        {
            get { return "Antivírus"; }
        }

        public override int EstimatedSeconds
        {
            get { return 3; }
        }

        protected override JObject Read(
            CollectionContext context, ErrorSink errors, CancellationToken cancellationToken)
        {
            bool? available;
            IList<PropertyBag> products;

            try
            {
                products = Wmi.Instances(Wmi.SecurityCenter2, "AntiVirusProduct");
                available = true;
            }
            catch (Exception exception)
            {
                // O namespace não existe em edições Server. Lá a ausência é da CENTRAL, não da
                // proteção — e é o cruzamento com o inventário de software que evita o falso
                // positivo (feito em Consolidation).
                errors.Record("root\\SecurityCenter2 AntiVirusProduct", exception);
                products = new List<PropertyBag>();
                available = false;
            }

            var defender = errors.Read("MSFT_MpComputerStatus",
                () => Wmi.Instances(Wmi.Defender, "MSFT_MpComputerStatus").FirstOrDefault());

            return AntivirusFacts.Build(available, products, defender);
        }

        protected override string Summarize(JObject data)
        {
            if (FlagOf(data["securityCenterAvailable"]) != true) return "Central de Segurança indisponível";

            var products = data["products"] as JArray;
            if (products == null || products.Count == 0) return "Nenhum produto registrado na Central de Segurança";

            var nomes = products.Select(product => TextOf(product["displayName"]));

            return string.Format("{0} produto(s): {1}", products.Count, string.Join(", ", nomes));
        }
    }

    /// <summary>Derivação pura do payload de <c>antivirus</c>.</summary>
    public static class AntivirusFacts
    {
        public static JObject Build(bool? securityCenterAvailable, IList<PropertyBag> products, PropertyBag defender)
        {
            products = products ?? new List<PropertyBag>();

            var entries = products.Select(Product).ToList();

            var data = new JObject();

            data["securityCenterAvailable"] = securityCenterAvailable;
            data["products"] = Payload.Array(entries);
            data["defender"] = Defender(defender);

            // Os dois campos abaixo enxergam o coletor de software e são preenchidos na
            // consolidação. Nulo aqui não é esquecimento: é "ainda não sabemos".
            data["securitySoftwareInInventory"] = null;
            data["anyProtectionDetected"] = null;

            data["activeProductCount"] = securityCenterAvailable == true ? (int?)entries.Count : null;

            // A MENOR confiança entre os produtos detectados. Enquanto a decodificação de
            // productState não for validada em campo, é None — e SEC-001/002/003 resolvem
            // Indeterminate por causa dela. É o requisito vinculante do doc 03 §4.6 tornado
            // verificável no dado, e não apenas na intenção de quem implementa.
            data["overallConfidence"] = entries.Count > 0 ? "None" : null;

            data["realtimeProtectionState"] = "Unknown";
            data["definitionsState"] = "Unknown";

            return Payload.Sanitized(data);
        }

        private static JObject Product(PropertyBag product)
        {
            var state = product.Int("productState");

            var entry = new JObject();

            entry["displayName"] = product.Text("displayName");

            // SEMPRE preservado cru. É o que permite reinterpretar relatórios antigos quando a
            // decodificação melhorar — sem voltar à máquina do cliente.
            entry["productStateRaw"] = state;
            entry["productStateHex"] = state.HasValue
                ? "0x" + state.Value.ToString("X", CultureInfo.InvariantCulture)
                : null;

            entry["timestamp"] = product.Text("timestamp");

            // productState é máscara de bits NÃO documentada pela Microsoft: toda decodificação
            // que circula é engenharia reversa da comunidade. Enquanto a Fase 1 não validar a
            // decodificação contra dezenas de máquinas reais, a confiança é None.
            entry["interpretation"] = new JObject
            {
                ["confidence"] = "None",
                ["enabled"] = "Unknown",
                ["realtimeProtection"] = "Unknown",
                ["definitions"] = "Unknown"
            };

            return entry;
        }

        private static JObject Defender(PropertyBag defender)
        {
            return new JObject
            {
                ["present"] = defender == null ? null : (bool?)true,
                ["amServiceEnabled"] = defender == null ? null : defender.Flag("AMServiceEnabled"),
                ["realtimeProtectionEnabled"] = defender == null ? null : defender.Flag("RealTimeProtectionEnabled"),
                ["antivirusSignatureAgeDays"] = defender == null ? null : defender.Int("AntivirusSignatureAge"),
                ["isTamperProtected"] = defender == null ? null : defender.Flag("IsTamperProtected")
            };
        }
    }
}
