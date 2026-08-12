using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace EpicoraCheckup.Collectors.Sources
{
    /// <summary>
    /// Leitura do registro. **Só leitura** — nada neste projeto escreve no registro, e a
    /// Fase 5 escreve em chave própria (ADR-007), nunca daqui.
    ///
    /// A visão é fixada em <see cref="RegistryView.Registry64"/> em vez de
    /// <see cref="RegistryView.Default"/>: o binário é x64 por ADR-001, mas depender do
    /// bitness do processo faria a leitura de <c>WOW6432Node</c> mudar de significado se
    /// alguém compilasse x86 um dia. Os caminhos de 32 bits são pedidos explicitamente,
    /// como no protótipo.
    /// </summary>
    public static class RegistryReader
    {
        public static bool KeyExists(RegistryHive hive, string path)
        {
            using (var key = OpenKey(hive, path))
                return key != null;
        }

        /// <summary>Valor cru, ou <c>null</c> quando a chave ou o valor não existem.</summary>
        public static object Value(RegistryHive hive, string path, string name)
        {
            using (var key = OpenKey(hive, path))
                return key == null ? null : key.GetValue(name);
        }

        /// <summary>
        /// Se o valor EXISTE, independente do conteúdo. Distingue-se de <see cref="Text"/>
        /// porque o protótipo trata "valor presente e vazio" como presente — é o caso de
        /// <c>WUServer</c>, em que existir a política já é a resposta.
        /// </summary>
        public static bool HasValue(RegistryHive hive, string path, string name)
        {
            return Value(hive, path, name) != null;
        }

        public static string Text(RegistryHive hive, string path, string name)
        {
            var value = Value(hive, path, name);
            if (value == null) return null;

            var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        public static int? Int(RegistryHive hive, string path, string name)
        {
            var value = Value(hive, path, name);
            if (value == null) return null;

            try
            {
                return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                // DWORD ausente é diferente de DWORD ilegível, mas para o campo dá no mesmo:
                // não sabemos, então null.
                return null;
            }
        }

        public static IList<string> SubKeyNames(RegistryHive hive, string path)
        {
            using (var key = OpenKey(hive, path))
                return key == null ? new List<string>() : new List<string>(key.GetSubKeyNames());
        }

        /// <summary>Todos os valores de uma chave. É como se lê as chaves <c>Run</c>.</summary>
        public static IDictionary<string, object> Values(RegistryHive hive, string path)
        {
            // Sem diferenciar maiúsculas: nome de valor no registro é insensível, e há
            // instalador que grava "displayname". Comparar por ordinal perderia o programa.
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            using (var key = OpenKey(hive, path))
            {
                if (key == null) return values;

                foreach (var name in key.GetValueNames())
                {
                    // O valor padrão da chave vem com nome vazio e não é item de inicialização.
                    if (string.IsNullOrEmpty(name)) continue;

                    // Sem expansão de REG_EXPAND_SZ: o comando é gravado como está no registro,
                    // e a expansão de variáveis acontece só na hora de localizar o executável
                    // para ler a assinatura.
                    values[name] = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                }
            }

            return values;
        }

        /// <summary>
        /// Uma chave de um caminho tipo <c>SOFTWARE\Microsoft\...</c>, ou <c>null</c>.
        ///
        /// Devolver null em vez de lançar é deliberado: chave ausente é o caso NORMAL em
        /// metade das leituras deste projeto — máquina sem política, sem RDP, sem SecureBoot.
        /// </summary>
        private static RegistryKey OpenKey(RegistryHive hive, string path)
        {
            RegistryKey baseKey = null;

            try
            {
                baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                return baseKey.OpenSubKey(path, false);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (baseKey != null) baseKey.Dispose();
            }
        }
    }
}
