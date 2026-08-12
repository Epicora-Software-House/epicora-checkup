using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EpicoraCheckup.Collectors.Sources;
using EpicoraCheckup.Core.Contracts;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Collectors.Collectors
{
    /// <summary>
    /// Bateria. <c>wearPercent</c> acima de 30% é achado de venda direta: serviço de ticket
    /// baixo e alta percepção de valor (HW-001, HW-002).
    /// </summary>
    public sealed class BatteryCollector : CollectorBase
    {
        public override string Id
        {
            get { return "battery"; }
        }

        public override string DisplayName
        {
            get { return "Bateria"; }
        }

        public override int EstimatedSeconds
        {
            get { return 3; }
        }

        /// <summary>Desktop não tem bateria. Ignorado não é falha nem achado negativo.</summary>
        protected override string SkipReason(CollectionContext context)
        {
            try
            {
                return Wmi.Instances(Wmi.CimV2, "Win32_Battery").Count == 0
                    ? "não aplicável a esta máquina"
                    : null;
            }
            catch (Exception)
            {
                return "não aplicável a esta máquina";
            }
        }

        protected override JObject Read(
            CollectionContext context, ErrorSink errors, CancellationToken cancellationToken)
        {
            var batteries = Wmi.Instances(Wmi.CimV2, "Win32_Battery");

            // Win32_Battery NÃO entrega capacidade nem ciclos: a sonda confirmou DesignCapacity
            // e FullChargeCapacity nulos e nenhuma propriedade de ciclos. Quem entrega:
            //
            //   root\wmi BatteryCycleCount.CycleCount       -> ciclos
            //   root\wmi BatteryFullChargedCapacity         -> carga plena, em mWh
            //   Win32_PortableBattery.DesignCapacity        -> capacidade de projeto, em mWh
            //
            // As três foram validadas contra powercfg /batteryreport na mesma máquina: ciclos e
            // carga plena bateram EXATAMENTE, projeto ficou a 1 mWh. Nada disto escreve arquivo,
            // que era a única razão de o powercfg estar fora do protótipo.
            var capacities = errors.Read("BatteryFullChargedCapacity",
                () => Wmi.Instances(Wmi.WmiNamespace, "BatteryFullChargedCapacity")) ?? new List<PropertyBag>();

            var cycles = errors.Read("BatteryCycleCount",
                () => Wmi.Instances(Wmi.WmiNamespace, "BatteryCycleCount")) ?? new List<PropertyBag>();

            var portable = errors.Read("Win32_PortableBattery",
                () => Wmi.Instances(Wmi.CimV2, "Win32_PortableBattery")) ?? new List<PropertyBag>();

            return BatteryFacts.Build(batteries, capacities, cycles, portable);
        }

        protected override string Summarize(JObject data)
        {
            var batteries = data["batteries"] as JArray;

            var first = batteries != null && batteries.Count > 0 ? batteries[0] : null;

            var ciclos = first == null || first["cycleCount"].Type == JTokenType.Null
                ? string.Empty
                : ", " + first["cycleCount"] + " ciclos";

            var desgaste = data["wearPercent"];

            return desgaste.Type == JTokenType.Null
                ? "Desgaste não pôde ser calculado" + ciclos
                : "Desgaste de " + desgaste + "%" + ciclos;
        }
    }

    /// <summary>Derivação pura do payload de <c>battery</c>.</summary>
    public static class BatteryFacts
    {
        private static readonly Dictionary<int, string> Chemistries = new Dictionary<int, string>
        {
            { 3, "Lead Acid" }, { 4, "NiCd" }, { 5, "NiMH" }, { 6, "Li-ion" }, { 8, "LiP" }
        };

        public static JObject Build(
            IList<PropertyBag> batteries,
            IList<PropertyBag> fullCharge,
            IList<PropertyBag> cycleCounts,
            IList<PropertyBag> portable)
        {
            // Só o par de classes root\wmi correlaciona por Tag — a sonda mostrou as duas com o
            // mesmo Tag e o mesmo InstanceName.
            var byTag = new SortedDictionary<string, RawBattery>(StringComparer.Ordinal);

            foreach (var item in fullCharge)
            {
                var tag = item.Text("Tag") ?? string.Empty;
                Entry(byTag, tag).FullChargeMwh = item.Int("FullChargedCapacity");
            }

            foreach (var item in cycleCounts)
            {
                var tag = item.Text("Tag") ?? string.Empty;
                Entry(byTag, tag).Cycles = item.Int("CycleCount");
            }

            var tags = byTag.Values.ToList();

            var entries = new List<JObject>();

            for (var index = 0; index < batteries.Count; index++)
            {
                var battery = batteries[index];

                // Correlação por POSIÇÃO. Com uma bateria — o caso normal — é exata. Com mais de
                // uma, só vale se a fonte trouxer a MESMA quantidade de instâncias; caso
                // contrário fica null, em vez de atribuir o dado de uma bateria à outra.
                int? full = null;
                int? cycles = null;

                if (tags.Count == batteries.Count && index < tags.Count)
                {
                    full = tags[index].FullChargeMwh;
                    cycles = tags[index].Cycles;
                }

                if (!full.HasValue) full = battery.Int("FullChargeCapacity");

                var design = battery.Int("DesignCapacity");
                if (!design.HasValue && portable.Count == batteries.Count && index < portable.Count)
                {
                    // NÃO multiplicar por CapacityMultiplier: DesignCapacity já vem em mWh.
                    // Verificado contra o powercfg — multiplicar daria 10x e desgaste negativo.
                    design = portable[index].Int("DesignCapacity");
                }

                var entry = new JObject();

                entry["name"] = battery.Text("Name");

                // Chemistry = 2 ('Unknown') é o que o hardware realmente reporta em campo.
                // Código fora do mapa vira null, não um palpite.
                entry["chemistry"] = Payload.Lookup(Chemistries, battery.Int("Chemistry"));
                entry["currentChargePercent"] = battery.Int("EstimatedChargeRemaining");
                entry["designCapacityMwh"] = design;
                entry["fullChargeCapacityMwh"] = full;
                entry["cycleCount"] = cycles;

                entries.Add(entry);
            }

            var first = entries.FirstOrDefault();

            var wear = first == null
                ? null
                : Wear((int?)first["designCapacityMwh"], (int?)first["fullChargeCapacityMwh"]);

            var data = new JObject();

            data["present"] = entries.Count > 0;
            data["batteries"] = Payload.ArrayOrNull(entries);
            data["wearPercent"] = wear;
            data["wearSource"] = wear.HasValue ? "wmi" : "unavailable";

            return Payload.Sanitized(data);
        }

        /// <summary>
        /// Desgaste: <c>1 − carga plena / capacidade de projeto</c>.
        ///
        /// Negativo é arredondado para zero — bateria nova costuma carregar acima do projeto, e
        /// "desgaste de −3%" num relatório de cliente parece defeito da ferramenta.
        /// </summary>
        public static double? Wear(int? designMwh, int? fullChargeMwh)
        {
            if (!designMwh.HasValue || !fullChargeMwh.HasValue || designMwh.Value <= 0) return null;
            if (fullChargeMwh.Value <= 0) return null;

            var wear = Math.Round((1 - fullChargeMwh.Value / (double)designMwh.Value) * 100, 1);

            return wear < 0 ? 0 : wear;
        }

        private static RawBattery Entry(IDictionary<string, RawBattery> map, string tag)
        {
            RawBattery entry;
            if (map.TryGetValue(tag, out entry)) return entry;

            entry = new RawBattery();
            map[tag] = entry;

            return entry;
        }

        private sealed class RawBattery
        {
            public int? FullChargeMwh { get; set; }

            public int? Cycles { get; set; }
        }
    }
}
