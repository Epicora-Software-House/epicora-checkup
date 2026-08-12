using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using EpicoraCheckup.Collectors.Sources;
using EpicoraCheckup.Core.Contracts;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Collectors.Collectors
{
    /// <summary>
    /// Rede. <c>linkDowngraded</c> é o gancho da vertical de infraestrutura: um achado por
    /// máquina que, repetido no parque, vira diagnóstico de cabeamento.
    /// </summary>
    public sealed class NetworkCollector : CollectorBase
    {
        public override string Id
        {
            get { return "network"; }
        }

        public override string DisplayName
        {
            get { return "Rede"; }
        }

        public override int EstimatedSeconds
        {
            get { return 4; }
        }

        protected override JObject Read(
            CollectionContext context, ErrorSink errors, CancellationToken cancellationToken)
        {
            var adapters = Wmi.Instances(Wmi.CimV2, "Win32_NetworkAdapter", "PhysicalAdapter=TRUE");
            var configurations = Wmi.Instances(Wmi.CimV2, "Win32_NetworkAdapterConfiguration");

            var extended = errors.Read("MSFT_NetAdapter",
                () => Wmi.Instances(Wmi.StandardCimV2, "MSFT_NetAdapter")) ?? new List<PropertyBag>();

            var computer = Wmi.Instances(Wmi.CimV2, "Win32_ComputerSystem").FirstOrDefault();
            var domainJoined = computer != null && computer.Flag("PartOfDomain") == true;

            return NetworkFacts.Build(adapters, configurations, extended, domainJoined);
        }

        protected override string Summarize(JObject data)
        {
            var primary = TextOf(data["primaryAdapterName"]);
            if (primary == null) return "Nenhum adaptador ativo identificado";

            var tipo = TextOf(data["primaryConnectionType"]);

            var rotulo = tipo == "Wired" ? "Cabo" : tipo == "Wireless" ? "Wi-Fi" : "Conexão";

            var adapters = data["adapters"] as JArray;

            var adapter = adapters == null
                ? null
                : adapters.FirstOrDefault(item => TextOf(item["name"]) == primary);

            var velocidade = adapter == null ? null : LongOf(adapter["linkSpeedBps"]);

            return velocidade.HasValue && velocidade.Value > 0
                ? rotulo + ", " + Math.Round(velocidade.Value / 1000000d) + " Mbps"
                : rotulo;
        }
    }

    /// <summary>Derivação pura do payload de <c>network</c>.</summary>
    public static class NetworkFacts
    {
        // MEDIDO EM CAMPO: MSFT_NetAdapter NÃO tem PhysicalMediaType, MediaType, LinkSpeed,
        // MacAddress nem Status no build testado — todas voltam ausentes. Quem existe e
        // responde é NdisPhysicalMedium (enum NDIS_PHYSICAL_MEDIUM). Valores confirmados pela
        // sonda: 9 = Native802_11 (Wi-Fi) e 14 = 802_3 (Ethernet).
        //
        // Antes disto 'Wired' era INALCANÇÁVEL: a propriedade lida não existia, o tipo caía em
        // Unknown e só o regex de descrição salvava — e ele só reconhece Wi-Fi. Máquina com
        // cabo saía Unknown e NET-002 perdia a base.
        private static readonly int[] WirelessMedium = { 1, 8, 9, 12 };

        private static readonly int[] WiredMedium = { 2, 3, 4, 5, 6, 7, 14, 15, 17, 18 };

        private const string VirtualPattern =
            @"(?i)virtual|hyper-v|vmware|virtualbox|loopback|tap-|tun\b|vpn|wintun|wireguard";

        private static readonly string[] PublicDnsPatterns =
        {
            @"^8\.8\.", @"^8\.8\.4\.", @"^1\.1\.1\.1$", @"^1\.0\.0\.1$",
            @"^9\.9\.9\.", @"^208\.67\.2", @"^94\.140\.14\."
        };

        private const long OneGigabit = 1000000000L;
        private const long HundredMegabit = 100000000L;

        public static JObject Build(
            IList<PropertyBag> adapters,
            IList<PropertyBag> configurations,
            IList<PropertyBag> extended,
            bool domainJoined)
        {
            var byIndex = new Dictionary<int, PropertyBag>();
            foreach (var configuration in configurations)
            {
                var index = configuration.Int("Index");
                if (index.HasValue) byIndex[index.Value] = configuration;
            }

            var byMac = new Dictionary<string, PropertyBag>(StringComparer.Ordinal);
            foreach (var item in extended)
            {
                var mac = item.Text("MacAddress");
                if (mac != null) byMac[NormalizeMac(mac)] = item;
            }

            var entries = new List<JObject>();
            JObject primary = null;

            foreach (var adapter in adapters)
            {
                var mac = adapter.Text("MACAddress");

                PropertyBag ext = null;
                if (mac != null) byMac.TryGetValue(NormalizeMac(mac), out ext);

                var index = adapter.Int("Index");

                PropertyBag configuration = null;
                if (index.HasValue) byIndex.TryGetValue(index.Value, out configuration);

                var description = (adapter.Text("Name") ?? string.Empty) + " " +
                                  (adapter.Text("Description") ?? string.Empty);

                var isVirtual = IsVirtual(ext, description);

                var addresses = new List<string>();
                var dns = new List<string>();
                string gateway = null;
                bool? dhcp = null;

                if (configuration != null)
                {
                    // fe80:: e 169.254.x são endereços de link local: existem em toda máquina e
                    // não dizem nada sobre a rede em que ela está.
                    addresses = configuration.Texts("IPAddress")
                        .Where(address => !Regex.IsMatch(address, @"^fe80|^169\.254"))
                        .ToList();

                    dns = configuration.Texts("DNSServerSearchOrder").ToList();
                    gateway = configuration.Texts("DefaultIPGateway").FirstOrDefault();
                    dhcp = configuration.Flag("DHCPEnabled");
                }

                // As DUAS fontes, nesta ordem, e não uma OU outra: a mesma medição de campo que
                // mostrou MSFT_NetAdapter sem LinkSpeed vale para Speed. Escolher a fonte pela
                // existência do objeto — e não pela existência do VALOR — deixava linkSpeedBps
                // nulo justamente onde NET-001 tem valor comercial.
                var linkSpeed = (ext == null ? null : ext.Long("Speed")) ?? adapter.Long("Speed");
                var maxSpeed = adapter.Long("MaxSpeed");

                // Placa gigabit que não declara MaxSpeed é a maioria. Sem este palpite,
                // linkDowngraded fica null justamente onde NET-001 tem valor comercial.
                if (!maxSpeed.HasValue && Regex.IsMatch(description, @"(?i)gigabit|gbe|\bi2[12]\d\b"))
                    maxSpeed = OneGigabit;

                var connected = adapter.Int("NetConnectionStatus") == 2;

                var entry = new JObject();

                entry["name"] = adapter.Text("NetConnectionID");
                entry["description"] = adapter.Text("Description");
                entry["macAddress"] = mac;
                entry["connected"] = connected;
                entry["isVirtual"] = isVirtual;
                entry["connectionType"] = ConnectionType(ext, isVirtual, description);
                entry["linkSpeedBps"] = linkSpeed;
                entry["maxSpeedBps"] = maxSpeed;
                entry["dhcpEnabled"] = dhcp;
                entry["ipAddresses"] = Payload.TextsOrNull(addresses);
                entry["defaultGateway"] = gateway;
                entry["dnsServers"] = Payload.TextsOrNull(dns);

                entries.Add(entry);

                // Primário é o primeiro que está conectado, não é virtual e tem gateway — o que
                // efetivamente leva o tráfego da máquina para fora.
                if (connected && !isVirtual && gateway != null && primary == null) primary = entry;
            }

            var data = new JObject();

            data["adapters"] = Payload.ArrayOrNull(entries);
            data["primaryAdapterName"] = primary == null ? null : (string)primary["name"];
            data["primaryConnectionType"] = primary == null ? null : (string)primary["connectionType"];
            data["linkDowngraded"] = LinkDowngraded(primary);
            data["publicDnsInDomainEnvironment"] = PublicDns(primary, domainJoined);
            data["staticIpConfigured"] = StaticIp(primary);

            return Payload.Sanitized(data);
        }

        /// <summary>
        /// Placa que negocia 100 Mbps podendo 1 Gbps. Quase sempre é cabo ruim, conector
        /// oxidado ou switch antigo — conserto barato com efeito imediato que o cliente sente.
        /// </summary>
        public static bool? LinkDowngraded(JObject primary)
        {
            if (primary == null) return null;

            var max = (long?)primary["maxSpeedBps"];
            var link = (long?)primary["linkSpeedBps"];

            if (!max.HasValue || !link.HasValue || link.Value <= 0) return null;

            return max.Value >= OneGigabit && link.Value <= HundredMegabit;
        }

        public static string ConnectionType(PropertyBag extended, bool isVirtual, string description)
        {
            if (isVirtual) return "Virtual";

            if (extended != null)
            {
                var medium = extended.Int("NdisPhysicalMedium");

                if (medium.HasValue)
                {
                    if (WirelessMedium.Contains(medium.Value)) return "Wireless";
                    if (WiredMedium.Contains(medium.Value)) return "Wired";
                }

                // Builds antigos podem expor PhysicalMediaType. Se existir, vale como segunda
                // opinião.
                var media = extended.Int("PhysicalMediaType");
                if (media.HasValue) return media.Value == 9 || media.Value == 16 ? "Wireless" : "Wired";
            }

            // Último recurso, quando o adaptador não casou por MAC com nenhum MSFT_NetAdapter.
            // Wi-Fi PRIMEIRO: "Wireless-AC Ethernet Adapter" existe e casaria no padrão de cabo.
            if (Regex.IsMatch(description, @"(?i)wi-?fi|wireless|802\.11|wlan")) return "Wireless";
            if (Regex.IsMatch(description, @"(?i)ethernet|gigabit|\bgbe\b")) return "Wired";

            return "Unknown";
        }

        private static bool IsVirtual(PropertyBag extended, string description)
        {
            if (extended != null && extended.Flag("Virtual") == true) return true;

            // Adaptador virtual é filtrado da apresentação mas REGISTRADO no JSON: presença de
            // adaptador de VPN é informação relevante sobre a máquina.
            return Regex.IsMatch(description, VirtualPattern);
        }

        private static bool? PublicDns(JObject primary, bool domainJoined)
        {
            if (!domainJoined || primary == null) return null;

            var servers = primary["dnsServers"] as JArray;
            if (servers == null) return null;

            return servers.Any(server => PublicDnsPatterns.Any(
                pattern => Regex.IsMatch((string)server, pattern)));
        }

        private static bool? StaticIp(JObject primary)
        {
            if (primary == null) return null;

            var dhcp = (bool?)primary["dhcpEnabled"];

            return dhcp.HasValue ? (bool?)!dhcp.Value : null;
        }

        private static string NormalizeMac(string mac)
        {
            return mac.Replace('-', ':').ToUpperInvariant();
        }
    }
}
