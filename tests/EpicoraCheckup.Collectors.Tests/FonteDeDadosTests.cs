using System;
using EpicoraCheckup.Collectors.Sources;
using Xunit;
using static EpicoraCheckup.Collectors.Tests.Fonte;

namespace EpicoraCheckup.Collectors.Tests
{
    /// <summary>
    /// Leitura de propriedade — a camada onde "campo vazio" vira <c>null</c> em vez de string
    /// vazia, e onde um valor ilegível vira <c>null</c> em vez de zero.
    ///
    /// Parece detalhe e não é: campo vazio virando <c>""</c> ou <c>0</c> destrói a análise no
    /// consolidador (doc 02 §5), porque "zero por cento livre" e "não medido" passam a ser o
    /// mesmo número.
    /// </summary>
    public sealed class FonteDeDadosTests
    {
        [Fact]
        public void Propriedade_ausente_e_texto_em_branco_viram_null()
        {
            var bag = Bag("Preenchida", "valor", "EmBranco", "   ", "Vazia", "");

            Assert.Equal("valor", bag.Text("Preenchida"));
            Assert.Null(bag.Text("EmBranco"));
            Assert.Null(bag.Text("Vazia"));
            Assert.Null(bag.Text("NuncaExistiu"));
        }

        [Fact]
        public void Nome_de_propriedade_nao_diferencia_maiusculas()
        {
            Assert.Equal("DELL", Bag("Manufacturer", "DELL").Text("manufacturer"));
        }

        [Fact]
        public void Numero_vem_como_texto_em_varias_classes_e_precisa_ser_lido_assim()
        {
            // DeviceId do MSFT_PhysicalDisk é o caso real: chega "0" e o schema exige inteiro.
            Assert.Equal(0, Bag("DeviceId", "0").Int("DeviceId"));
            Assert.Equal(1000204886016L, Bag("Size", (ulong)1000204886016L).Long("Size"));
        }

        [Fact]
        public void Valor_ilegivel_vira_null_e_nunca_zero()
        {
            Assert.Null(Bag("Size", "não é número").Long("Size"));
            Assert.Null(Bag("Codigo", new object()).Int("Codigo"));
        }

        [Fact]
        public void Booleano_chega_como_bool_como_numero_e_como_texto()
        {
            Assert.True(Bag("Enabled", true).Flag("Enabled"));
            Assert.True(Bag("Enabled", (ushort)1).Flag("Enabled"));
            Assert.False(Bag("Enabled", (ushort)0).Flag("Enabled"));
            Assert.True(Bag("Enabled", "TRUE").Flag("Enabled"));
            Assert.Null(Bag("Outra", true).Flag("Enabled"));
        }

        [Fact]
        public void Texto_escalar_nao_vira_lista_de_letras()
        {
            // String é IEnumerable de char: sem desvio, um IP único viraria "1", "9", "2"...
            var enderecos = Bag("IPAddress", "192.168.0.10").Texts("IPAddress");

            Assert.Single(enderecos);
            Assert.Equal("192.168.0.10", enderecos[0]);
        }

        [Fact]
        public void Vetor_de_texto_vem_inteiro()
        {
            var enderecos = Bag("IPAddress", new[] { "192.168.0.10", "fe80::1" }).Texts("IPAddress");

            Assert.Equal(2, enderecos.Count);
        }

        [Theory]
        [InlineData("20260729181835.000000-180", 2026, 7, 29, 18, 18, 35, -180)]
        [InlineData("20240101000000.000000+000", 2024, 1, 1, 0, 0, 0, 0)]
        [InlineData("20251231235959.999999+330", 2025, 12, 31, 23, 59, 59, 330)]
        public void CIM_DATETIME_e_lido_com_o_fuso_em_MINUTOS(
            string valor, int ano, int mes, int dia, int hora, int minuto, int segundo, int offsetMinutos)
        {
            var momento = PropertyBag.ParseCimDateTime(valor);

            Assert.NotNull(momento);
            Assert.Equal(new DateTimeOffset(ano, mes, dia, hora, minuto, segundo,
                TimeSpan.FromMinutes(offsetMinutos)), momento.Value);
        }

        [Fact]
        public void Campo_de_texto_ausente_sai_como_nulo_de_verdade_e_nao_como_texto_nulo()
        {
            // ARMADILHA MEDIDA, e das caras: atribuir um string nulo a um JObject produz um
            // token de tipo String com conteúdo nulo, não um token nulo. Serializado dá no
            // mesmo; em memória, NÃO — o motor decide disponibilidade por
            // Type == JTokenType.Null, e passaria a tratar campo não coletado como campo
            // preenchido. Uma falha de coleta viraria achado avaliado em vez de Indeterminate,
            // que é a regra 1 de contribuição violada.
            var dados = Collectors.MachineFacts.Build(
                computer: Bag("Name", "MAQUINA", "PartOfDomain", true),
                product: null, bios: null, baseboard: null, enclosure: null,
                hasBattery: false, now: System.DateTimeOffset.Now);

            Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Null, dados["manufacturer"].Type);
            Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Null, dados["bios"]["version"].Type);
            Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Null, dados["workgroup"].Type);
        }

        [Theory]
        [InlineData("00000000000000.000000+000")]   // BIOS sem data preenchida
        [InlineData("20260231181835.000000-180")]   // 31 de fevereiro, de firmware ruim
        [InlineData("29/07/2026")]                  // não é CIM_DATETIME
        [InlineData("")]
        [InlineData(null)]
        public void Data_impossivel_vira_null_em_vez_de_excecao(string valor)
        {
            Assert.Null(PropertyBag.ParseCimDateTime(valor));
        }
    }
}
