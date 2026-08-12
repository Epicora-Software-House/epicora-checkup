using EpicoraCheckup.Collectors.Collectors;
using Newtonsoft.Json.Linq;
using Xunit;
using static EpicoraCheckup.Collectors.Tests.Fonte;

namespace EpicoraCheckup.Collectors.Tests
{
    /// <summary>
    /// Rede — NET-001 e NET-002.
    ///
    /// A máquina de campo mostrou que <c>MSFT_NetAdapter</c> não traz
    /// <c>PhysicalMediaType</c>, <c>MediaType</c>, <c>LinkSpeed</c> nem <c>Status</c> neste
    /// build. Quem responde é <c>NdisPhysicalMedium</c>, e sem ele "Wired" era inalcançável.
    /// </summary>
    public sealed class RedeTests
    {
        [Fact]
        public void Cabo_e_reconhecido_por_NdisPhysicalMedium_14()
        {
            var ext = Bag("NdisPhysicalMedium", (uint)14);

            Assert.Equal("Wired", NetworkFacts.ConnectionType(ext, false, "Realtek PCIe GbE Family Controller"));
        }

        [Fact]
        public void WiFi_e_reconhecido_por_NdisPhysicalMedium_9()
        {
            var ext = Bag("NdisPhysicalMedium", (uint)9);

            Assert.Equal("Wireless", NetworkFacts.ConnectionType(ext, false, "Intel Wi-Fi 6 AX201 160MHz"));
        }

        [Fact]
        public void Sem_NdisPhysicalMedium_a_descricao_decide_e_WiFi_vem_primeiro()
        {
            // "Wireless-AC Ethernet Adapter" existe e casaria no padrão de cabo. Testar Wi-Fi
            // antes é o que impede classificar uma placa sem fio como cabeada — e NET-002 fala
            // de máquina que deveria estar no cabo.
            Assert.Equal("Wireless", NetworkFacts.ConnectionType(null, false, "Wireless-AC 9560 Ethernet Adapter"));
            Assert.Equal("Wired", NetworkFacts.ConnectionType(null, false, "Realtek Gigabit Ethernet"));
            Assert.Equal("Unknown", NetworkFacts.ConnectionType(null, false, "Adaptador de rede genérico"));
        }

        [Fact]
        public void Adaptador_virtual_nao_e_classificado_por_meio_fisico()
        {
            Assert.Equal("Virtual", NetworkFacts.ConnectionType(
                Bag("NdisPhysicalMedium", (uint)14), true, "Hyper-V Virtual Ethernet Adapter"));
        }

        [Fact]
        public void Placa_gigabit_negociando_100_Mbps_e_o_achado_de_cabeamento()
        {
            var primario = new JObject
            {
                ["maxSpeedBps"] = 1000000000L,
                ["linkSpeedBps"] = 100000000L
            };

            Assert.True(NetworkFacts.LinkDowngraded(primario));
        }

        [Fact]
        public void Sem_velocidade_negociada_a_conclusao_e_null_e_nao_false()
        {
            var primario = new JObject
            {
                ["maxSpeedBps"] = 1000000000L,
                ["linkSpeedBps"] = JValue.CreateNull()
            };

            Assert.Null(NetworkFacts.LinkDowngraded(primario));
            Assert.Null(NetworkFacts.LinkDowngraded(null));
        }

        [Fact]
        public void Adaptador_primario_e_o_conectado_nao_virtual_com_gateway()
        {
            var dados = NetworkFacts.Build(
                adapters: Lista(
                    Bag("Index", (uint)1, "NetConnectionID", "VPN corporativa", "Description", "TAP-Windows Adapter V9",
                        "MACAddress", "00:FF:11:22:33:44", "NetConnectionStatus", (ushort)2),
                    Bag("Index", (uint)2, "NetConnectionID", "Ethernet", "Description", "Realtek PCIe GbE Family Controller",
                        "MACAddress", "AA:BB:CC:DD:EE:FF", "NetConnectionStatus", (ushort)2,
                        "Speed", (ulong)100000000L, "MaxSpeed", (ulong)1000000000L)),
                configurations: Lista(
                    Bag("Index", (uint)1, "IPAddress", new[] { "10.8.0.2" }, "DHCPEnabled", false),
                    Bag("Index", (uint)2, "IPAddress", new[] { "192.168.0.10", "fe80::1" },
                        "DefaultIPGateway", new[] { "192.168.0.1" },
                        "DNSServerSearchOrder", new[] { "8.8.8.8" }, "DHCPEnabled", true)),
                extended: Lista(
                    Bag("MacAddress", "AA-BB-CC-DD-EE-FF", "NdisPhysicalMedium", (uint)14, "Virtual", false)),
                domainJoined: true);

            Assert.Equal("Ethernet", (string)dados["primaryAdapterName"]);
            Assert.Equal("Wired", (string)dados["primaryConnectionType"]);
            Assert.True((bool)dados["linkDowngraded"]);
            Assert.False((bool)dados["staticIpConfigured"]);

            // DNS público em máquina de domínio é achado de configuração (NET-004).
            Assert.True((bool)dados["publicDnsInDomainEnvironment"]);

            // Endereço de link local não conta como endereço da máquina.
            Assert.Single(dados["adapters"][1]["ipAddresses"]);

            // O adaptador de VPN é filtrado da escolha do primário mas REGISTRADO no JSON:
            // presença de VPN é informação relevante sobre a máquina.
            Assert.True((bool)dados["adapters"][0]["isVirtual"]);
        }
    }
}
