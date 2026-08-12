using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EpicoraCheckup.Collectors.Sources;
using EpicoraCheckup.Core.Contracts;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Collectors.Collectors
{
    /// <summary>Identificação da máquina: fabricante, modelo, série, BIOS e idade aproximada.</summary>
    public sealed class MachineCollector : CollectorBase
    {
        public override string Id
        {
            get { return "machine"; }
        }

        public override string DisplayName
        {
            get { return "Identificação da máquina"; }
        }

        public override int EstimatedSeconds
        {
            get { return 2; }
        }

        protected override JObject Read(
            CollectionContext context, ErrorSink errors, CancellationToken cancellationToken)
        {
            var computer = Wmi.Instances(Wmi.CimV2, "Win32_ComputerSystem").FirstOrDefault();
            var product = Wmi.Instances(Wmi.CimV2, "Win32_ComputerSystemProduct").FirstOrDefault();
            var bios = Wmi.Instances(Wmi.CimV2, "Win32_BIOS").FirstOrDefault();

            var baseboard = errors.Read("Win32_BaseBoard",
                () => Wmi.Instances(Wmi.CimV2, "Win32_BaseBoard").FirstOrDefault());

            var enclosure = errors.Read("Win32_SystemEnclosure",
                () => Wmi.Instances(Wmi.CimV2, "Win32_SystemEnclosure").FirstOrDefault());

            var batteries = errors.Read("Win32_Battery",
                () => Wmi.Instances(Wmi.CimV2, "Win32_Battery"));

            return MachineFacts.Build(
                computer, product, bios, baseboard, enclosure,
                batteries != null && batteries.Count > 0,
                DateTimeOffset.Now);
        }

        protected override string Summarize(JObject data)
        {
            var laptop = FlagOf(data["isLaptop"]);

            var tipo = laptop == true ? "Notebook" : laptop == false ? "Desktop" : "Máquina";

            return string.Join(" ", new[] { tipo, TextOf(data["manufacturer"]), TextOf(data["model"]) }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
        }
    }

    /// <summary>Derivação pura do payload de <c>machine</c>, testável sem WMI.</summary>
    public static class MachineFacts
    {
        /// <summary>
        /// Mapa PARCIAL e deliberado de tipos de chassi. Código fora dele vira <c>null</c>,
        /// não um palpite.
        /// </summary>
        private static readonly Dictionary<int, string> ChassisNames = new Dictionary<int, string>
        {
            { 3, "Desktop" }, { 4, "Low Profile Desktop" }, { 5, "Pizza Box" }, { 6, "Mini Tower" },
            { 7, "Tower" }, { 8, "Portable" }, { 9, "Laptop" }, { 10, "Notebook" },
            { 11, "Hand Held" }, { 13, "All in One" }, { 14, "Sub Notebook" }, { 15, "Space-saving" },
            { 16, "Lunch Box" }, { 17, "Main System Chassis" }, { 23, "Rack Mount Chassis" },
            { 30, "Tablet" }, { 31, "Convertible" }, { 32, "Detachable" }
        };

        private static readonly int[] PortableChassis = { 8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32 };

        public static JObject Build(
            PropertyBag computer,
            PropertyBag product,
            PropertyBag bios,
            PropertyBag baseboard,
            PropertyBag enclosure,
            bool hasBattery,
            DateTimeOffset now)
        {
            var chassis = enclosure == null ? new List<int>() : enclosure.Ints("ChassisTypes");

            var partOfDomain = computer == null ? null : computer.Flag("PartOfDomain");
            var domainJoined = partOfDomain ?? false;

            var biosDate = bios == null ? null : bios.Moment("ReleaseDate");
            double? ageYears = biosDate.HasValue
                ? (double?)Math.Round((now - biosDate.Value).TotalDays / 365.25, 1)
                : null;

            var laptop = InferLaptop(chassis, hasBattery);

            var data = new JObject();

            data["hostname"] = computer == null ? null : computer.Text("Name");
            data["domainJoined"] = domainJoined;
            data["domain"] = domainJoined && computer != null ? computer.Text("Domain") : null;
            data["workgroup"] = !domainJoined && computer != null ? computer.Text("Workgroup") : null;
            data["manufacturer"] = computer == null ? null : computer.Text("Manufacturer");
            data["model"] = computer == null ? null : computer.Text("Model");
            data["uuid"] = product == null ? null : product.Text("UUID");
            data["productSerial"] = product == null ? null : product.Text("IdentifyingNumber");
            data["chassisTypes"] = chassis.Count == 0
                ? (JToken)JValue.CreateNull()
                : Payload.Numbers(chassis);
            data["chassisTypeName"] = chassis.Count == 0
                ? null
                : Payload.Lookup(ChassisNames, chassis[0]);
            data["isLaptop"] = laptop.IsLaptop;
            data["isLaptopBasis"] = laptop.Basis;

            data["bios"] = new JObject
            {
                ["manufacturer"] = bios == null ? null : bios.Text("Manufacturer"),
                ["version"] = bios == null ? null : bios.Text("SMBIOSBIOSVersion"),
                ["serial"] = bios == null ? null : bios.Text("SerialNumber"),
                ["releaseDate"] = Payload.Date(biosDate)
            };

            data["baseboard"] = new JObject
            {
                ["manufacturer"] = baseboard == null ? null : baseboard.Text("Manufacturer"),
                ["product"] = baseboard == null ? null : baseboard.Text("Product"),
                ["serial"] = baseboard == null ? null : baseboard.Text("SerialNumber")
            };

            // Idade é APROXIMAÇÃO declarada, nunca fato: BIOS atualizado altera a data. O
            // relatório sempre escreve "aproximadamente", e marcação manual do técnico
            // prevalece (doc 03 §4.9).
            data["approxAgeYears"] = ageYears;
            data["approxAgeBasis"] = ageYears.HasValue ? "biosReleaseDate" : null;

            return Payload.Sanitized(data);
        }

        /// <summary>
        /// Notebook ou desktop.
        ///
        /// O chassi é mal preenchido por vários fabricantes (doc 02 §4.1), então a bateria é a
        /// confirmação secundária — e ganha na discordância: desktop com bateria é raro,
        /// notebook com chassi errado não. Sem chassi e sem bateria fica <c>null</c>, porque
        /// "não achei bateria" também acontece em notebook com bateria removida.
        ///
        /// Importa comercialmente: separa SEC-004 (notebook sem BitLocker, Alto) de SEC-005
        /// (desktop, Médio). Notebook sai da empresa; desktop não.
        /// </summary>
        public static LaptopVerdict InferLaptop(IList<int> chassis, bool hasBattery)
        {
            if (chassis == null || chassis.Count == 0)
            {
                return hasBattery
                    ? new LaptopVerdict(true, "battery")
                    : new LaptopVerdict(null, null);
            }

            var chassisSaysLaptop = chassis.Any(code => PortableChassis.Contains(code));

            return chassisSaysLaptop == hasBattery
                ? new LaptopVerdict(chassisSaysLaptop, "both")
                : new LaptopVerdict(hasBattery, "conflict");
        }

        public sealed class LaptopVerdict
        {
            public LaptopVerdict(bool? isLaptop, string basis)
            {
                IsLaptop = isLaptop;
                Basis = basis;
            }

            public bool? IsLaptop { get; }

            public string Basis { get; }
        }
    }
}
