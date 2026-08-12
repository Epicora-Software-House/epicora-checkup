using System;
using System.Collections.Generic;
using System.Management;

namespace EpicoraCheckup.Collectors.Sources
{
    /// <summary>
    /// Consulta WMI. É o único lugar do projeto que fala com <see cref="System.Management"/>;
    /// todo o resto trabalha sobre <see cref="PropertyBag"/>, que é dado morto.
    ///
    /// **Nunca consultar <c>Win32_Product</c>.** A classe dispara reconfiguração de todo
    /// pacote MSI da máquina do cliente. Proibição do doc 02 §4.7 e da regra 2 de
    /// contribuição — não é preferência de performance. O inventário de software sai do
    /// registro, em <see cref="Collectors.SoftwareCollector"/>.
    /// </summary>
    public static class Wmi
    {
        public const string CimV2 = @"root\CIMV2";
        public const string Storage = @"root\Microsoft\Windows\Storage";
        public const string StandardCimV2 = @"root\StandardCimv2";
        public const string WmiNamespace = @"root\wmi";
        public const string SecurityCenter2 = @"root\SecurityCenter2";
        public const string Tpm = @"root\CIMV2\Security\MicrosoftTpm";
        public const string VolumeEncryption = @"root\CIMV2\Security\MicrosoftVolumeEncryption";
        public const string Defender = @"root\Microsoft\Windows\Defender";
        public const string TaskScheduler = @"root\Microsoft\Windows\TaskScheduler";

        public static IList<PropertyBag> Instances(string wmiNamespace, string className)
        {
            return Query(wmiNamespace, "SELECT * FROM " + className);
        }

        public static IList<PropertyBag> Instances(string wmiNamespace, string className, string where)
        {
            return Query(wmiNamespace, "SELECT * FROM " + className + " WHERE " + where);
        }

        /// <summary>
        /// Roda a consulta e devolve o retrato de cada instância.
        ///
        /// Não engole exceção: quem chama decide se a falha é fatal para o coletor inteiro
        /// ou se degrada um campo para null. Engolir aqui apagaria a distinção entre
        /// "namespace não existe nesta edição" e "não sei", que é o que separa
        /// <c>false</c> de <c>null</c> em vários campos do schema.
        /// </summary>
        public static IList<PropertyBag> Query(string wmiNamespace, string query)
        {
            var results = new List<PropertyBag>();

            var options = new EnumerationOptions
            {
                // O padrão devolve propriedades herdadas e amenities que não usamos.
                ReturnImmediately = true,
                Rewindable = false
            };

            using (var searcher = new ManagementObjectSearcher(
                new ManagementScope(wmiNamespace), new ObjectQuery(query), options))
            using (var collection = searcher.Get())
            {
                foreach (ManagementBaseObject instance in collection)
                {
                    try
                    {
                        results.Add(Snapshot(instance));
                    }
                    finally
                    {
                        instance.Dispose();
                    }
                }
            }

            return results;
        }

        /// <summary>Copia propriedades e classe para um objeto desconectado da fonte.</summary>
        private static PropertyBag Snapshot(ManagementBaseObject instance)
        {
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in instance.Properties)
            {
                if (property == null || property.Name == null) continue;
                values[property.Name] = Convert(property.Value);
            }

            return new PropertyBag(ClassNameOf(instance), values);
        }

        /// <summary>
        /// Instâncias embutidas viram <see cref="PropertyBag"/> também. É o que permite ler os
        /// gatilhos de uma tarefa agendada, cuja única marca é o nome da classe.
        /// </summary>
        private static object Convert(object value)
        {
            var embedded = value as ManagementBaseObject;
            if (embedded != null)
            {
                using (embedded) return Snapshot(embedded);
            }

            var array = value as ManagementBaseObject[];
            if (array != null)
            {
                var list = new List<object>(array.Length);
                foreach (var item in array)
                {
                    if (item == null) continue;
                    using (item) list.Add(Snapshot(item));
                }

                return list;
            }

            return value;
        }

        private static string ClassNameOf(ManagementBaseObject instance)
        {
            try
            {
                var path = instance.ClassPath;
                if (path != null && !string.IsNullOrEmpty(path.ClassName)) return path.ClassName;
            }
            catch (ManagementException)
            {
                // Objeto embutido pode não ter caminho resolvível. O __CLASS abaixo resolve.
            }

            try
            {
                return System.Convert.ToString(instance.SystemProperties["__CLASS"].Value);
            }
            catch (ManagementException)
            {
                return null;
            }
        }
    }
}
