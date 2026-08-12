using EpicoraCheckup.Collectors.Collectors;
using Newtonsoft.Json.Linq;
using Xunit;
using static EpicoraCheckup.Collectors.Tests.Fonte;

namespace EpicoraCheckup.Collectors.Tests
{
    /// <summary>
    /// Bateria — HW-001 e HW-002, desgaste acima de 30% é venda direta.
    ///
    /// A sonda mediu que <c>Win32_Battery</c> não entrega capacidade nem ciclos, e que as
    /// classes de <c>root\wmi</c> entregam — batendo exatamente com o
    /// <c>powercfg /batteryreport</c> da mesma máquina.
    /// </summary>
    public sealed class BateriaTests
    {
        [Fact]
        public void Desgaste_sai_da_razao_entre_carga_plena_e_projeto()
        {
            Assert.Equal(25.0, BatteryFacts.Wear(designMwh: 60000, fullChargeMwh: 45000));
        }

        [Fact]
        public void Bateria_nova_que_carrega_acima_do_projeto_nao_produz_desgaste_negativo()
        {
            // "Desgaste de −3%" num relatório de cliente parece defeito da ferramenta.
            Assert.Equal(0.0, BatteryFacts.Wear(designMwh: 60000, fullChargeMwh: 61800));
        }

        [Theory]
        [InlineData(null, 45000)]
        [InlineData(60000, null)]
        [InlineData(0, 45000)]
        public void Sem_um_dos_lados_o_desgaste_e_null_e_nao_zero(int? projeto, int? plena)
        {
            Assert.Null(BatteryFacts.Wear(projeto, plena));
        }

        [Fact]
        public void Ciclos_e_carga_plena_vem_das_classes_de_root_wmi()
        {
            var dados = BatteryFacts.Build(
                batteries: Lista(Bag(
                    "Name", "DELL 4GVMP", "Chemistry", (ushort)2,
                    "EstimatedChargeRemaining", (ushort)87)),
                fullCharge: Lista(Bag("Tag", "1", "FullChargedCapacity", (uint)45230)),
                cycleCounts: Lista(Bag("Tag", "1", "CycleCount", (uint)312)),
                portable: Lista(Bag("DesignCapacity", (uint)60000)));

            var bateria = dados["batteries"][0];

            Assert.Equal(45230, (int)bateria["fullChargeCapacityMwh"]);
            Assert.Equal(312, (int)bateria["cycleCount"]);
            Assert.Equal(60000, (int)bateria["designCapacityMwh"]);
            Assert.Equal(24.6, (double)dados["wearPercent"]);
            Assert.Equal("wmi", (string)dados["wearSource"]);

            // Chemistry = 2 ("Unknown") é o que o hardware reporta de verdade em campo, e não
            // está no mapa: vira null em vez de um palpite.
            Assert.Equal(JTokenType.Null, bateria["chemistry"].Type);
        }

        [Fact]
        public void Com_duas_baterias_e_uma_leitura_so_a_correlacao_nao_e_arriscada()
        {
            // Correlação por posição só vale quando a fonte traz a MESMA quantidade de
            // instâncias. Atribuir o dado de uma bateria à outra é pior que não atribuir.
            var dados = BatteryFacts.Build(
                batteries: Lista(Bag("Name", "Bateria 1"), Bag("Name", "Bateria 2")),
                fullCharge: Lista(Bag("Tag", "1", "FullChargedCapacity", (uint)45230)),
                cycleCounts: Lista(Bag("Tag", "1", "CycleCount", (uint)312)),
                portable: Nenhum());

            Assert.Equal(JTokenType.Null, dados["batteries"][0]["cycleCount"].Type);
            Assert.Equal(JTokenType.Null, dados["batteries"][1]["cycleCount"].Type);
            Assert.Equal(JTokenType.Null, dados["wearPercent"].Type);
            Assert.Equal("unavailable", (string)dados["wearSource"]);
        }

        [Fact]
        public void Sem_as_classes_de_root_wmi_ainda_vale_o_que_o_Win32_Battery_der()
        {
            // Sessão sem privilégio pode perder root\wmi. O relatório sai parcial, não vazio.
            var dados = BatteryFacts.Build(
                batteries: Lista(Bag(
                    "Name", "Bateria genérica", "FullChargeCapacity", (uint)41000,
                    "DesignCapacity", (uint)50000, "Chemistry", (ushort)6)),
                fullCharge: Nenhum(), cycleCounts: Nenhum(), portable: Nenhum());

            var bateria = dados["batteries"][0];

            Assert.Equal("Li-ion", (string)bateria["chemistry"]);
            Assert.Equal(18.0, (double)dados["wearPercent"]);
            Assert.Equal(JTokenType.Null, bateria["cycleCount"].Type);
        }
    }
}
