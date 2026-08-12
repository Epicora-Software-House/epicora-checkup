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
    /// Memória. <c>freeSlots</c> é um dos campos mais valiosos do produto: separa MEM-003
    /// (upgrade barato de pente) de MEM-004 (trocar a máquina), e permite orçar sem abrir o
    /// gabinete.
    /// </summary>
    public sealed class MemoryCollector : CollectorBase
    {
        public override string Id
        {
            get { return "memory"; }
        }

        public override string DisplayName
        {
            get { return "Memória"; }
        }

        public override int EstimatedSeconds
        {
            get { return 3; }
        }

        protected override JObject Read(
            CollectionContext context, ErrorSink errors, CancellationToken cancellationToken)
        {
            var computer = Wmi.Instances(Wmi.CimV2, "Win32_ComputerSystem").FirstOrDefault();
            var modules = Wmi.Instances(Wmi.CimV2, "Win32_PhysicalMemory");

            var array = errors.Read("Win32_PhysicalMemoryArray",
                () => Wmi.Instances(Wmi.CimV2, "Win32_PhysicalMemoryArray").FirstOrDefault());

            return MemoryFacts.Build(computer, modules, array);
        }

        protected override string Summarize(JObject data)
        {
            var free = data["freeSlots"];

            var slots = free == null || free.Type == JTokenType.Null
                ? "slots não verificados"
                : free + " slots livres";

            return string.Format("{0} em {1} pente(s), {2}",
                FormatBytes(LongOf(data["totalBytes"])), data["usedSlots"], slots);
        }
    }

    /// <summary>Derivação pura do payload de <c>memory</c>.</summary>
    public static class MemoryFacts
    {
        private const long Gibibyte = 1073741824L;
        private const long Tebibyte = 1099511627776L;

        private static readonly Dictionary<int, string> MemoryTypes = new Dictionary<int, string>
        {
            { 20, "DDR" }, { 21, "DDR2" }, { 24, "DDR3" }, { 26, "DDR4" },
            { 34, "DDR5" }, { 35, "LPDDR4" }, { 36, "LPDDR5" }
        };

        public static JObject Build(PropertyBag computer, IList<PropertyBag> modules, PropertyBag array)
        {
            modules = modules ?? new List<PropertyBag>();

            var totalBytes = computer == null ? null : computer.Long("TotalPhysicalMemory");

            // Arredondar para GiB inteiro é requisito, não conveniência: máquina de 4 GB
            // reporta menos que 4 GiB exatos porque parte da memória é do vídeo, e comparar
            // bytes crus contra limiar redondo erra MEM-001.
            int? totalGiB = totalBytes.HasValue
                ? (int?)(int)Math.Round(totalBytes.Value / (double)Gibibyte, 0)
                : null;

            var totalSlots = array == null ? null : array.Int("MemoryDevices");
            var usedSlots = modules.Count;

            int? freeSlots = totalSlots.HasValue && totalSlots.Value >= usedSlots
                ? (int?)(totalSlots.Value - usedSlots)
                : null;

            var entries = modules.Select(Module).ToList();

            var speeds = entries
                .Select(module => module["speedMhz"])
                .Where(speed => speed != null && speed.Type != JTokenType.Null)
                .Select(speed => (long)speed)
                .Distinct()
                .ToList();

            var data = new JObject();

            data["totalBytes"] = totalBytes;
            data["totalGiB"] = totalGiB;
            data["totalSlots"] = totalSlots;
            data["usedSlots"] = usedSlots;
            data["freeSlots"] = freeSlots;
            data["maxCapacityBytes"] = MaxCapacityBytes(array);
            data["speedMismatch"] = speeds.Count == 0 ? null : (bool?)(speeds.Count > 1);
            data["modules"] = Payload.ArrayOrNull(entries);

            return Payload.Sanitized(data);
        }

        /// <summary>
        /// Capacidade máxima suportada pela placa.
        ///
        /// <c>MaxCapacity</c> é notoriamente mal preenchido: aparece zerado, ou com valores
        /// absurdos como 2 PB. Fora da faixa plausível vira <c>null</c> — **nunca zero**, que
        /// no consolidador viraria "esta máquina não aceita memória".
        /// </summary>
        public static long? MaxCapacityBytes(PropertyBag array)
        {
            if (array == null) return null;

            // MaxCapacityEx existe porque MaxCapacity é uint32 em KB e estoura em 4 TB.
            var kilobytes = array.Long("MaxCapacityEx") ?? array.Long("MaxCapacity");
            if (!kilobytes.HasValue || kilobytes.Value <= 0) return null;

            var bytes = kilobytes.Value * 1024L;

            return bytes >= Gibibyte && bytes <= 8L * Tebibyte ? (long?)bytes : null;
        }

        private static JObject Module(PropertyBag module)
        {
            // SMBIOSMemoryType é a fonte boa; MemoryType é o campo antigo, e vale só quando o
            // primeiro vem ausente ou zerado.
            var code = module.Int("SMBIOSMemoryType");
            var source = "SMBIOSMemoryType";

            if (!code.HasValue || code.Value == 0)
            {
                code = module.Int("MemoryType");
                source = "MemoryType";
            }

            var entry = new JObject();

            entry["capacityBytes"] = module.Long("Capacity");
            entry["speedMhz"] = module.Int("Speed");
            entry["configuredSpeedMhz"] = module.Int("ConfiguredClockSpeed");
            entry["manufacturer"] = module.Text("Manufacturer");
            entry["partNumber"] = module.Trimmed("PartNumber");
            entry["bankLabel"] = module.Text("BankLabel");
            entry["deviceLocator"] = module.Text("DeviceLocator");
            entry["memoryTypeCode"] = code;
            entry["memoryTypeName"] = Payload.Lookup(MemoryTypes, code);
            entry["memoryTypeSource"] = code.HasValue ? source : null;

            return entry;
        }
    }
}
