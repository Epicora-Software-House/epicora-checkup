using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace EpicoraCheckup.Rules
{
    /// <summary>
    /// Configuração de serialização do JSON de saída, num só lugar.
    ///
    /// A forma do JSON é contrato: o consolidador lê estes arquivos, e os golden files
    /// em tests/expected/ são comparados contra esta serialização. Espalhar
    /// JsonSerializerSettings pelo código é como o formato começa a divergir entre
    /// quem grava o relatório e quem grava a fixture.
    /// </summary>
    public static class CheckupJson
    {
        public static JsonSerializerSettings Settings
        {
            get
            {
                return new JsonSerializerSettings
                {
                    // camelCase nas propriedades, mas NÃO nas chaves de dicionário: as
                    // chaves de Finding.Evidence são caminhos pontilhados do documento
                    // de coleta e têm que sair literais.
                    ContractResolver = new DefaultContractResolver
                    {
                        NamingStrategy = new CamelCaseNamingStrategy
                        {
                            ProcessDictionaryKeys = false,
                            OverrideSpecifiedNames = true
                        }
                    },

                    // Enums como texto: "Critical", "NonCompliant". Número no JSON tornaria
                    // o arquivo ilegível e quebraria o consolidador ao reordenar um enum.
                    Converters = { new StringEnumConverter() },

                    // Campo ausente é null explícito, nunca omitido — o consolidador
                    // distingue "não coletado" de "não existe no schema desta versão".
                    NullValueHandling = NullValueHandling.Include,

                    Formatting = Formatting.Indented
                };
            }
        }

        public static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, Settings);
        }
    }
}
