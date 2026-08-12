using System.Collections.Generic;
using EpicoraCheckup.Collectors.Collectors;
using EpicoraCheckup.Collectors.Sources;
using Newtonsoft.Json.Linq;
using Xunit;
using static EpicoraCheckup.Collectors.Tests.Fonte;

namespace EpicoraCheckup.Collectors.Tests
{
    /// <summary>
    /// Armazenamento — o coletor que sustenta STO-004 (Crítico) e a venda de SSD.
    ///
    /// A máquina de campo nº 2 (JULIA-LAPTOP, 2 discos) rendeu o bug que estes testes
    /// existem para não deixar voltar.
    /// </summary>
    public sealed class ArmazenamentoTests
    {
        // InstanceName real, na forma que o driver publica: o modelo do disco aparece
        // fatiado por "Ven_" e "Prod_", e por isso o casamento é palavra a palavra.
        private const string InstanciaWd = @"SCSI\Disk&Ven_WDC&Prod_WD10SPZX-21Z10T0\5&1ca0da9&0&000000_0";
        private const string InstanciaSanDisk = @"SCSI\Disk&Ven_&Prod_SanDisk_SD9SN8U256G\4&1a2b3c4&0&000000_0";
        private const string InstanciaNvme = @"SCSI\Disk&Ven_NVMe&Prod_Samsung_SSD_980\5&35b0e12&0&000000_0";

        [Fact]
        public void Notebook_com_dois_discos_atribui_o_SMART_ao_disco_certo()
        {
            // O caso que falhava em campo: SSD de sistema + HD de dados. O guard antigo exigia
            // exatamente um disco E uma leitura, então os DOIS ficavam sem failurePredicted com
            // o dado SMART em mãos.
            var discos = new List<string> { "SanDisk SD9SN8U256G", "WDC WD10SPZX-21Z10T0" };

            var leituras = new Dictionary<string, bool>
            {
                { InstanciaWd, true },
                { InstanciaSanDisk, false }
            };

            var resolvido = SmartCorrelation.Resolve(discos, leituras);

            Assert.Equal(2, resolvido.Count);
            Assert.False(resolvido[0]);
            Assert.True(resolvido[1]);
        }

        [Fact]
        public void Modelo_com_capacidade_que_o_InstanceName_nao_traz_fica_sem_leitura()
        {
            // LIMITE CONHECIDO do casamento palavra a palavra, registrado aqui em vez de
            // descoberto em campo: o FriendlyName de vários discos traz a capacidade
            // ("Samsung SSD 980 500GB") e o InstanceName não ("Prod_Samsung_SSD_980"). A
            // palavra "500GB" não casa e o disco fica sem failurePredicted.
            //
            // Fica null de propósito. Afrouxar para "casou a maioria das palavras" é o que
            // permitiria atribuir a leitura de um disco ao outro numa máquina com dois da mesma
            // família — e STO-004 é Crítico. Vale mais Indeterminate.
            //
            // Na prática pesa pouco: MSStorageDriver_FailurePredictStatus cobre ATA/SATA, e
            // disco NVMe raramente aparece nessa classe.
            var resolvido = SmartCorrelation.Resolve(
                new List<string> { "Samsung SSD 980 500GB" },
                new Dictionary<string, bool> { { InstanciaNvme, true } });

            Assert.Empty(resolvido);
        }

        [Fact]
        public void Dois_discos_de_modelo_identico_ficam_sem_leitura_em_vez_de_apontar_o_errado()
        {
            // Modelos iguais em duas baias é configuração real. Apontar falha prevista no disco
            // saudável manda trocar o disco errado e deixa o que está morrendo na máquina —
            // pior que Indeterminate.
            var discos = new List<string> { "WDC WD10SPZX-21Z10T0", "WDC WD10SPZX-21Z10T0" };

            var leituras = new Dictionary<string, bool> { { InstanciaWd, true } };

            Assert.Empty(SmartCorrelation.Resolve(discos, leituras));
        }

        [Fact]
        public void Duas_leituras_para_o_mesmo_disco_tambem_ficam_sem_atribuicao()
        {
            var discos = new List<string> { "WDC WD10SPZX-21Z10T0" };

            var leituras = new Dictionary<string, bool>
            {
                { InstanciaWd, true },
                { InstanciaWd + "_2", false }
            };

            Assert.Empty(SmartCorrelation.Resolve(discos, leituras));
        }

        [Fact]
        public void Palavra_curta_do_modelo_nao_serve_para_casar()
        {
            // "WD" aparece em QUALQUER InstanceName de disco Western Digital, inclusive no do
            // outro disco da mesma marca.
            Assert.False(SmartCorrelation.Matches(InstanciaWd, "WD"));
        }

        [Theory]
        [InlineData("NTFS DisableDeleteNotify = 0  (Permite que operações TRIM sejam enviadas)", true)]
        [InlineData("NTFS DisableDeleteNotify = 1  (Desabilitado)", false)]
        [InlineData("DisableDeleteNotify = 0", true)]
        [InlineData("ReFS DisableDeleteNotify = 1", null)]
        [InlineData("", null)]
        [InlineData("O parâmetro está incorreto.", null)]
        public void TRIM_e_lido_pelo_numero_porque_a_prosa_do_fsutil_e_traduzida(string saida, bool? esperado)
        {
            // A polaridade é invertida no nome da chave: 0 = notificação de exclusão HABILITADA,
            // ou seja TRIM ligado.
            Assert.Equal(esperado, TrimQuery.Parse(saida));
        }

        [Fact]
        public void Disco_de_sistema_e_o_que_hospeda_o_volume_de_sistema_e_nao_o_primeiro_da_lista()
        {
            var dados = StorageFacts.Build(
                physical: Lista(
                    Disco("0", "WDC WD10SPZX-21Z10T0", 3, 1000204886016L),
                    Disco("1", "Samsung SSD 980 500GB", 4, 500107862016L)),
                legacy: Nenhum(),
                smart: Nenhum(),
                volumes: Lista(Volume("C:", 400000000000L, 40000000000L)),
                partitions: Lista(Bag("DriveLetter", "C", "DiskNumber", (uint)1)),
                disks: Nenhum(),
                systemDrive: "C:",
                trimEnabled: true,
                windowsOldPresent: false);

            var disco = dados["systemDisk"];

            Assert.Equal("Samsung SSD 980 500GB", (string)disco["model"]);
            Assert.Equal("SSD", (string)disco["mediaType"]);
            Assert.Equal(10.0, (double)dados["systemVolume"]["freePercent"]);
        }

        [Fact]
        public void Sem_MSFT_PhysicalDisk_o_tipo_de_midia_fica_Unknown_e_nao_e_adivinhado()
        {
            // Win32_DiskDrive.MediaType devolve "Fixed hard disk media" também para SSD. Chutar
            // a partir dela é a armadilha proibida pelo doc 02 §4.3 — e Unknown aqui é o que
            // bloqueia OPT-DEFRAG na Fase 5.
            var dados = StorageFacts.Build(
                physical: Nenhum(),
                legacy: Lista(Bag(
                    "Index", (uint)0,
                    "Model", "SanDisk SD9SN8U256G",
                    "Size", 256060514304L,
                    "InterfaceType", "IDE",
                    "MediaType", "Fixed hard disk media")),
                smart: Nenhum(),
                volumes: Nenhum(),
                partitions: Nenhum(),
                disks: Nenhum(),
                systemDrive: "C:",
                trimEnabled: null,
                windowsOldPresent: false);

            var disco = dados["physicalDisks"][0];

            Assert.Equal("Unknown", (string)disco["mediaType"]);
            Assert.Equal("unavailable", (string)disco["mediaTypeSource"]);
        }

        [Fact]
        public void Midia_fora_do_mapa_vira_Unknown_em_vez_de_valor_fora_do_schema()
        {
            // 5 é SCM (memória persistente). O schema admite HDD, SSD e Unknown — inventar um
            // quarto valor deixaria o JSON fora do contrato e quebraria o consolidador.
            var dados = StorageFacts.Build(
                physical: Lista(Disco("0", "Intel Optane", 5, 1000000000L)),
                legacy: Nenhum(), smart: Nenhum(), volumes: Nenhum(),
                partitions: Nenhum(), disks: Nenhum(),
                systemDrive: "C:", trimEnabled: null, windowsOldPresent: false);

            Assert.Equal("Unknown", (string)dados["physicalDisks"][0]["mediaType"]);
        }

        [Fact]
        public void Um_disco_so_dispensa_a_correlacao_por_particao()
        {
            var dados = StorageFacts.Build(
                physical: Lista(Disco("0", "SanDisk SD9SN8U256G", 4, 256060514304L)),
                legacy: Nenhum(),
                smart: Lista(Bag("InstanceName", InstanciaSanDisk, "PredictFailure", true)),
                volumes: Nenhum(),
                partitions: Nenhum(),
                disks: Nenhum(),
                systemDrive: "C:",
                trimEnabled: null,
                windowsOldPresent: null);

            Assert.True((bool)dados["systemDisk"]["failurePredicted"]);
        }

        private static PropertyBag Disco(string deviceId, string modelo, int midia, long tamanho)
        {
            // DeviceId do MSFT_PhysicalDisk vem STRING; o schema exige inteiro.
            return Bag(
                "DeviceId", deviceId,
                "FriendlyName", modelo,
                "SerialNumber", "  S4EVNF0N123456      ",
                "Size", (ulong)tamanho,
                "MediaType", (ushort)midia,
                "BusType", (ushort)17,
                "HealthStatus", (ushort)0);
        }

        private static PropertyBag Volume(string letra, long tamanho, long livre)
        {
            return Bag(
                "DeviceID", letra,
                "VolumeName", "Windows",
                "FileSystem", "NTFS",
                "Size", (ulong)tamanho,
                "FreeSpace", (ulong)livre);
        }
    }
}
