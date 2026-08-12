using System;
using System.Collections.Generic;
using EpicoraCheckup.Collectors.Collectors;
using EpicoraCheckup.Collectors.Sources;
using Newtonsoft.Json.Linq;
using Xunit;
using static EpicoraCheckup.Collectors.Tests.Fonte;

namespace EpicoraCheckup.Collectors.Tests
{
    /// <summary>
    /// Identificação da máquina, processador, memória, sistema e atualizações — os campos que
    /// o relatório usa para dizer QUE máquina é esta e quanto ela tem de vida útil.
    /// </summary>
    public sealed class InventarioTests
    {
        private static readonly DateTimeOffset Agora =
            new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.FromHours(-3));

        // ---------------------------------------------------------------- notebook ou desktop

        [Fact]
        public void Chassi_e_bateria_de_acordo_respondem_com_base_nos_dois()
        {
            var veredito = MachineFacts.InferLaptop(new List<int> { 10 }, hasBattery: true);

            Assert.True(veredito.IsLaptop);
            Assert.Equal("both", veredito.Basis);
        }

        [Fact]
        public void Na_discordancia_a_bateria_vence_o_chassi()
        {
            // Chassi é mal preenchido por vários fabricantes. Desktop com bateria é raro;
            // notebook com chassi errado, não. Separa SEC-004 (Alto) de SEC-005 (Médio).
            var veredito = MachineFacts.InferLaptop(new List<int> { 3 }, hasBattery: true);

            Assert.True(veredito.IsLaptop);
            Assert.Equal("conflict", veredito.Basis);
        }

        [Fact]
        public void Sem_chassi_e_sem_bateria_a_resposta_e_null()
        {
            // "Não achei bateria" também acontece em notebook com a bateria removida.
            var veredito = MachineFacts.InferLaptop(new List<int>(), hasBattery: false);

            Assert.Null(veredito.IsLaptop);
            Assert.Null(veredito.Basis);
        }

        [Fact]
        public void Idade_aproximada_sai_da_data_do_BIOS_e_e_declarada_como_aproximacao()
        {
            var dados = MachineFacts.Build(
                computer: Bag("Name", "DELL-G15", "PartOfDomain", false, "Workgroup", "WORKGROUP",
                    "Manufacturer", "Dell Inc.", "Model", "G15 5510"),
                product: Bag("UUID", "4C4C4544-0031", "IdentifyingNumber", "ABC1234"),
                bios: Bag("Manufacturer", "Dell Inc.", "SMBIOSBIOSVersion", "1.14.0",
                    "ReleaseDate", "20230715000000.000000+000"),
                baseboard: Bag("Manufacturer", "Dell Inc.", "Product", "0X1Y2Z"),
                enclosure: Bag("ChassisTypes", new ushort[] { 10 }),
                hasBattery: true,
                now: Agora);

            Assert.Equal("Notebook", (string)dados["chassisTypeName"]);
            Assert.Equal("2023-07-15", (string)dados["bios"]["releaseDate"]);
            Assert.Equal(3.1, (double)dados["approxAgeYears"]);
            Assert.Equal("biosReleaseDate", (string)dados["approxAgeBasis"]);

            // Fora de domínio, o workgroup é que vale — e o domínio fica nulo, não vazio.
            Assert.Equal(JTokenType.Null, dados["domain"].Type);
            Assert.Equal("WORKGROUP", (string)dados["workgroup"]);
        }

        // ---------------------------------------------------------------- processador

        [Theory]
        [InlineData("Intel(R) Core(TM) i7-10750H CPU @ 2.60GHz", "Intel Core i7-10750H")]
        [InlineData("AMD Ryzen 5 5600G with Radeon Graphics", "AMD Ryzen 5 5600G")]
        [InlineData("11th Gen Intel(R) Core(TM) i5-1135G7 @ 2.40GHz", "Intel Core i5-1135G7")]
        [InlineData("Intel(R) Celeron(R) CPU N4020 @ 1.10GHz", "Intel Celeron N4020")]
        [InlineData("   ", null)]
        public void Nome_do_processador_e_normalizado_para_casar_com_a_lista_oficial(string cru, string esperado)
        {
            Assert.Equal(esperado, CpuFacts.Normalize(cru));
        }

        [Fact]
        public void Compatibilidade_de_CPU_nasce_null_com_o_motivo_declarado()
        {
            // Sem a lista oficial embutida (ADR-006), o par null + basis diz "não avaliado".
            // NUNCA "não suportado" — a diferença decide entre migrar e trocar a máquina.
            var dados = CpuFacts.Build(Bag("Name", "Intel(R) Core(TM) i5-8250U CPU @ 1.60GHz",
                "NumberOfCores", (uint)4, "AddressWidth", (ushort)64));

            Assert.Equal(JTokenType.Null, dados["win11Supported"].Type);
            Assert.Equal("listMissing", (string)dados["win11SupportBasis"]);
            Assert.Equal("x64", (string)dados["architecture"]);
        }

        // ---------------------------------------------------------------- memória

        [Fact]
        public void Total_em_GiB_e_arredondado_porque_parte_da_memoria_fica_com_o_video()
        {
            // Máquina "de 4 GB" reporta menos que 4 GiB exatos. Comparar bytes crus contra
            // limiar redondo erra MEM-001.
            var dados = MemoryFacts.Build(
                computer: Bag("TotalPhysicalMemory", (ulong)4187590656L),
                modules: Lista(Modulo(4294967296L, 2667, 26)),
                array: Bag("MemoryDevices", (uint)2, "MaxCapacityEx", (ulong)33554432L));

            Assert.Equal(4, (int)dados["totalGiB"]);
            Assert.Equal(1, (int)dados["usedSlots"]);
            Assert.Equal(1, (int)dados["freeSlots"]);
            Assert.Equal(34359738368L, (long)dados["maxCapacityBytes"]);
            Assert.Equal("DDR4", (string)dados["modules"][0]["memoryTypeName"]);
        }

        [Theory]
        [InlineData(0L)]                  // fabricante que não preenche
        [InlineData(1L)]                  // 1 KiB de capacidade máxima
        [InlineData(9007199254740L)]      // 8 PB
        public void Capacidade_maxima_absurda_vira_null_e_nunca_zero(long kilobytes)
        {
            // Zero no consolidador viraria "esta máquina não aceita memória", que é uma frase
            // que ninguém pode dizer para um cliente por causa de firmware mal preenchido.
            Assert.Null(MemoryFacts.MaxCapacityBytes(Bag("MaxCapacityEx", (ulong)kilobytes)));
        }

        [Fact]
        public void Velocidades_diferentes_entre_pentes_sao_marcadas()
        {
            var dados = MemoryFacts.Build(
                computer: Bag("TotalPhysicalMemory", (ulong)17179869184L),
                modules: Lista(Modulo(8589934592L, 2667, 26), Modulo(8589934592L, 2400, 26)),
                array: Bag("MemoryDevices", (uint)2));

            Assert.True((bool)dados["speedMismatch"]);
        }

        // ---------------------------------------------------------------- sistema

        [Theory]
        [InlineData(26100, false, "Windows 11")]
        [InlineData(19045, false, "Windows 10")]
        [InlineData(7601, false, "Windows 7")]
        [InlineData(20348, true, "Windows Server")]
        [InlineData(0, false, "Unknown")]
        public void Familia_do_sistema_sai_da_build_e_nao_do_caption_traduzido(
            int build, bool servidor, string esperado)
        {
            Assert.Equal(esperado, OsFacts.Family(build, servidor));
        }

        [Fact]
        public void Edicao_Home_e_reconhecida_pelo_EditionID_do_registro()
        {
            Assert.True(OsFacts.IsHome("Core"));
            Assert.True(OsFacts.IsHome("CoreSingleLanguage"));
            Assert.False(OsFacts.IsHome("Professional"));
            Assert.Null(OsFacts.IsHome(null));
        }

        [Fact]
        public void Sem_resposta_de_licenciamento_a_ativacao_fica_Unknown_e_nunca_nao_ativado()
        {
            // Acusar de pirataria uma máquina licenciada é o pior falso positivo que este
            // relatório pode produzir numa reunião com o cliente.
            var dados = OsFacts.Build(
                os: Bag("Caption", "Microsoft Windows 11 Pro", "BuildNumber", "26100",
                    "ProductType", (uint)1, "Version", "10.0.26100",
                    "InstallDate", "20240310120000.000000-180",
                    "LastBootUpTime", "20260810080000.000000-180"),
                currentVersion: new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    { "EditionID", "Professional" }, { "UBR", 3915 }, { "DisplayVersion", "24H2" }
                },
                activation: null,
                now: Agora);

            Assert.Equal("Unknown", (string)dados["activation"]["status"]);
            Assert.Equal(JTokenType.Null, dados["activation"]["statusCode"].Type);
            Assert.Equal("Windows 11", (string)dados["productFamily"]);
            Assert.Equal(3915, (int)dados["ubr"]);
            Assert.Equal("24H2", (string)dados["displayVersion"]);
            Assert.False((bool)dados["buildFreshness"]["evaluated"]);
        }

        // ---------------------------------------------------------------- atualizações

        [Theory]
        [InlineData("20250612000000.000000-180")]   // CIM_DATETIME
        [InlineData("20250612")]                    // compacto
        public void Data_de_hotfix_e_lida_nos_formatos_que_a_classe_devolve(string valor)
        {
            var data = HotfixDate.Parse(valor, Agora);

            Assert.NotNull(data);
            Assert.Equal(new DateTime(2025, 6, 12), data.Value);
        }

        [Fact]
        public void Data_de_hotfix_tambem_e_lida_quando_vem_como_DateTime()
        {
            Assert.Equal(new DateTime(2025, 6, 12), HotfixDate.Parse(new DateTime(2025, 6, 12), Agora));
        }

        [Theory]
        [InlineData("não é data")]
        [InlineData("19700101")]      // antes de o Windows 7 existir
        [InlineData("20990101")]      // no futuro
        public void Data_de_hotfix_implausivel_vira_null(string valor)
        {
            // Data errada aqui vira "esta máquina está há dois anos sem atualizar" num
            // relatório entregue ao cliente.
            Assert.Null(HotfixDate.Parse(valor, Agora));
        }

        [Fact]
        public void Ultima_atualizacao_e_a_mais_recente_da_lista()
        {
            var dados = UpdatesFacts.Build(
                hotfixes: Lista(
                    Bag("HotFixID", "KB5039211", "InstalledOn", "20250612"),
                    Bag("HotFixID", "KB5041585", "InstalledOn", "20250802"),
                    Bag("HotFixID", "KB5000000", "InstalledOn", "")),
                serviceEnabled: true,
                wsusConfigured: false,
                now: Agora);

            Assert.Equal("2025-08-02", (string)dados["lastUpdateDate"]);
            Assert.Equal(374, (int)dados["daysSinceLastUpdate"]);

            // A cobertura é SEMPRE parcial: a classe não lista cumulativas modernas, e nenhuma
            // regra pode concluir "desatualizado" só a partir daqui.
            Assert.True((bool)dados["coverageIsPartial"]);
        }

        // ---------------------------------------------------------------- Windows 11

        [Theory]
        [InlineData("2.0, 0, 1.38", 2.0)]
        [InlineData("1.2, 2, 3", 1.2)]
        [InlineData("não informado", null)]
        [InlineData(null, null)]
        public void Versao_do_TPM_sai_do_primeiro_componente_do_SpecVersion(string cru, double? esperado)
        {
            Assert.Equal(esperado, Win11Facts.SpecVersion(cru));
        }

        [Theory]
        [InlineData("UEFI", "UEFI")]
        [InlineData("Legacy", "Legacy")]
        [InlineData("", "Unknown")]
        [InlineData(null, "Unknown")]
        public void Modo_de_firmware_vem_da_variavel_de_ambiente_quando_ela_existe(
            string variavel, string esperado)
        {
            Assert.Equal(esperado, Win11Facts.FirmwareMode(variavel));
        }

        private static PropertyBag Modulo(long capacidade, int velocidade, int tipo)
        {
            return Bag(
                "Capacity", (ulong)capacidade,
                "Speed", (uint)velocidade,
                "ConfiguredClockSpeed", (uint)velocidade,
                "Manufacturer", "Samsung",
                "PartNumber", "M471A1K43CB1-CTD  ",
                "SMBIOSMemoryType", (uint)tipo);
        }
    }
}
