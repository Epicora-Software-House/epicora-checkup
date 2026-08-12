using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using EpicoraCheckup.Collectors.Sources;
using EpicoraCheckup.Core.Contracts;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Collectors.Collectors
{
    /// <summary>
    /// Armazenamento e saúde de disco. É o coletor que sustenta os dois achados de maior
    /// valor comercial do produto: <c>systemDisk.mediaType</c> (venda de SSD) e
    /// <c>systemDisk.failurePredicted</c> (troca urgente, STO-004 Crítico).
    ///
    /// Não é marcado <c>RequiresElevation</c>: só a leitura SMART exige privilégio, e ela
    /// degrada sozinha para null. Gatear o coletor inteiro perderia tipo de mídia, espaço
    /// livre e TRIM em toda visita sem senha de administrador.
    /// </summary>
    public sealed class StorageCollector : CollectorBase
    {
        public override string Id
        {
            get { return "storage"; }
        }

        public override string DisplayName
        {
            get { return "Armazenamento e saúde de disco"; }
        }

        public override int EstimatedSeconds
        {
            get { return 6; }
        }

        protected override JObject Read(
            CollectionContext context, ErrorSink errors, CancellationToken cancellationToken)
        {
            var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";

            // Tipo de mídia SÓ vem daqui. Win32_DiskDrive.MediaType devolve "Fixed hard disk
            // media" também para SSD — armadilha clássica, proibida pelo doc 02 §4.3.
            var physical = errors.Read("MSFT_PhysicalDisk",
                () => Wmi.Instances(Wmi.Storage, "MSFT_PhysicalDisk")) ?? new List<PropertyBag>();

            var legacy = physical.Count > 0
                ? new List<PropertyBag>()
                : errors.Read("Win32_DiskDrive", () => Wmi.Instances(Wmi.CimV2, "Win32_DiskDrive"))
                  ?? new List<PropertyBag>();

            // Exige elevação. Sem ela, cai aqui e failurePredicted fica null — STO-004 resolve
            // Indeterminate, que é a degradação correta.
            var smart = errors.Read("MSStorageDriver_FailurePredictStatus", () =>
                Wmi.Instances(Wmi.WmiNamespace, "MSStorageDriver_FailurePredictStatus"))
                ?? new List<PropertyBag>();

            var volumes = Wmi.Instances(Wmi.CimV2, "Win32_LogicalDisk", "DriveType=3");

            var partitions = errors.Read("MSFT_Partition",
                () => Wmi.Instances(Wmi.Storage, "MSFT_Partition")) ?? new List<PropertyBag>();

            var disksWmi = errors.Read("MSFT_Disk",
                () => Wmi.Instances(Wmi.Storage, "MSFT_Disk")) ?? new List<PropertyBag>();

            var trim = errors.Read("fsutil behavior query DisableDeleteNotify",
                () => TrimQuery.Parse(ConsoleTool.Run("fsutil.exe", "behavior query DisableDeleteNotify", 5000)));

            var windowsOld = errors.Read("Windows.old",
                () => (bool?)Directory.Exists(Path.Combine(systemDrive + Path.DirectorySeparatorChar, "Windows.old")));

            return StorageFacts.Build(physical, legacy, smart, volumes, partitions, disksWmi,
                systemDrive, trim, windowsOld);
        }

        protected override string Summarize(JObject data)
        {
            var disk = data["systemDisk"] as JObject;
            if (disk == null) return "Disco de sistema não identificado";

            var volume = data["systemVolume"] as JObject;

            var livre = volume == null
                ? "espaço não verificado"
                : volume["freePercent"] + "% livre";

            return string.Format("Disco de sistema: {0} {1}, {2}",
                TextOf(disk["mediaType"]), FormatBytes(LongOf(disk["sizeBytes"])), livre);
        }
    }

    /// <summary>Derivação pura do payload de <c>storage</c>.</summary>
    public static class StorageFacts
    {
        // 5 é SCM (memória persistente). O schema só admite HDD, SSD e Unknown, e inventar um
        // quarto valor deixaria o JSON fora do contrato — vira Unknown, como qualquer código
        // que não sabemos classificar.
        private static readonly Dictionary<int, string> MediaTypes = new Dictionary<int, string>
        {
            { 3, "HDD" }, { 4, "SSD" }
        };

        private static readonly Dictionary<int, string> BusTypes = new Dictionary<int, string>
        {
            { 1, "SCSI" }, { 2, "ATAPI" }, { 3, "ATA" }, { 4, "1394" }, { 5, "SSA" },
            { 6, "Fibre Channel" }, { 7, "USB" }, { 8, "RAID" }, { 9, "iSCSI" },
            { 10, "SAS" }, { 11, "SATA" }, { 12, "SD" }, { 13, "MMC" }, { 17, "NVMe" }
        };

        private static readonly Dictionary<int, string> HealthStates = new Dictionary<int, string>
        {
            { 0, "Healthy" }, { 1, "Warning" }, { 2, "Unhealthy" }
        };

        public static JObject Build(
            IList<PropertyBag> physical,
            IList<PropertyBag> legacy,
            IList<PropertyBag> smart,
            IList<PropertyBag> volumes,
            IList<PropertyBag> partitions,
            IList<PropertyBag> disks,
            string systemDrive,
            bool? trimEnabled,
            bool? windowsOldPresent)
        {
            var entries = physical.Count > 0
                ? physical.Select(ModernDisk).ToList()
                : legacy.Select(LegacyDisk).ToList();

            AttachSmart(entries, smart);

            var volumeEntries = volumes.Select(volume => Volume(volume, systemDrive)).ToList();

            var systemVolume = volumeEntries.FirstOrDefault(
                volume => (bool?)volume["isSystemVolume"] == true);

            var systemDisk = SystemDisk(entries, partitions, systemDrive);

            var data = new JObject();

            data["physicalDisks"] = Payload.ArrayOrNull(entries);
            data["volumes"] = Payload.ArrayOrNull(volumeEntries);

            data["systemDisk"] = systemDisk == null ? (JToken)JValue.CreateNull() : new JObject
            {
                ["model"] = systemDisk["model"],
                ["sizeBytes"] = systemDisk["sizeBytes"],
                ["mediaType"] = systemDisk["mediaType"],
                ["busType"] = systemDisk["busType"],
                ["healthStatus"] = systemDisk["healthStatus"],
                ["failurePredicted"] = systemDisk["failurePredicted"],
                ["trimEnabled"] = trimEnabled,

                // Análise de fragmentação de volume leva minutos. A coleta inteira tem 90 s
                // (doc 01 §4), então o campo fica null de propósito, não por esquecimento.
                ["fragmentationPercent"] = null,
                ["partitionStyle"] = PartitionStyle(disks)
            };

            data["systemVolume"] = systemVolume == null ? (JToken)JValue.CreateNull() : new JObject
            {
                ["driveLetter"] = systemVolume["driveLetter"],
                ["sizeBytes"] = systemVolume["sizeBytes"],
                ["freeBytes"] = systemVolume["freeBytes"],
                ["freePercent"] = systemVolume["freePercent"]
            };

            data["windowsOldPresent"] = windowsOldPresent;

            // Medir o tamanho exigiria varrer a árvore inteira, que é lento demais para os 90 s.
            data["windowsOldSizeBytes"] = null;

            return Payload.Sanitized(data);
        }

        private static JObject ModernDisk(PropertyBag disk)
        {
            // DeviceId do MSFT_PhysicalDisk vem STRING ("0") e o schema exige inteiro. Parse
            // tolerante: valor inesperado deixa o índice null em vez de derrubar o coletor.
            var entry = new JObject();

            entry["index"] = disk.Int("DeviceId");
            entry["model"] = disk.Text("FriendlyName");
            entry["serial"] = disk.Trimmed("SerialNumber");
            entry["sizeBytes"] = disk.Long("Size");
            entry["interfaceType"] = null;
            entry["mediaType"] = Payload.Lookup(MediaTypes, disk.Int("MediaType")) ?? "Unknown";
            entry["mediaTypeSource"] = "MSFT_PhysicalDisk";
            entry["busType"] = Payload.Lookup(BusTypes, disk.Int("BusType"));
            entry["healthStatus"] = Payload.Lookup(HealthStates, disk.Int("HealthStatus")) ?? "Unknown";
            entry["failurePredicted"] = null;

            return entry;
        }

        /// <summary>
        /// Sem <c>MSFT_PhysicalDisk</c>, registramos o disco mas **não adivinhamos o tipo de
        /// mídia**: <c>Unknown</c> com origem <c>unavailable</c>. É o caso em que "não sei" tem
        /// que bloquear a ação — OPT-DEFRAG da Fase 5 não roda sem tipo confirmado.
        /// </summary>
        private static JObject LegacyDisk(PropertyBag disk)
        {
            var entry = new JObject();

            entry["index"] = disk.Int("Index");
            entry["model"] = disk.Text("Model");
            entry["serial"] = disk.Trimmed("SerialNumber");
            entry["sizeBytes"] = disk.Long("Size");
            entry["interfaceType"] = disk.Text("InterfaceType");
            entry["mediaType"] = "Unknown";
            entry["mediaTypeSource"] = "unavailable";
            entry["busType"] = null;
            entry["healthStatus"] = "Unknown";
            entry["failurePredicted"] = null;

            return entry;
        }

        private static JObject Volume(PropertyBag volume, string systemDrive)
        {
            var size = volume.Long("Size");
            var free = volume.Long("FreeSpace");
            var letter = volume.Text("DeviceID");

            var entry = new JObject();

            entry["driveLetter"] = letter;
            entry["label"] = volume.Text("VolumeName");
            entry["fileSystem"] = volume.Text("FileSystem");
            entry["sizeBytes"] = size;
            entry["freeBytes"] = free;
            entry["freePercent"] = size.HasValue && size.Value > 0 && free.HasValue
                ? (double?)Math.Round(free.Value / (double)size.Value * 100, 1)
                : null;
            entry["isSystemVolume"] = string.Equals(letter, systemDrive, StringComparison.OrdinalIgnoreCase);

            return entry;
        }

        /// <summary>
        /// O disco FÍSICO que hospeda o volume de sistema — não "qualquer disco". É o que
        /// STO-001, STO-004 e STO-005 avaliam: um HD de dados velho não torna a máquina lenta,
        /// um disco de sistema HDD sim.
        /// </summary>
        private static JObject SystemDisk(
            IList<JObject> disks, IList<PropertyBag> partitions, string systemDrive)
        {
            var letter = systemDrive == null ? null : systemDrive.TrimEnd(':');

            var partition = partitions.FirstOrDefault(
                item => string.Equals(item.Text("DriveLetter"), letter, StringComparison.OrdinalIgnoreCase));

            if (partition != null)
            {
                var number = partition.Int("DiskNumber");

                var match = disks.FirstOrDefault(disk => (int?)disk["index"] == number);
                if (match != null) return match;
            }

            // Máquina de um disco só não precisa da correlação para ser respondida.
            return disks.Count == 1 ? disks[0] : null;
        }

        private static string PartitionStyle(IList<PropertyBag> disks)
        {
            var system = disks.FirstOrDefault(disk => disk.Flag("IsSystem") == true);
            if (system == null) return null;

            var style = system.Int("PartitionStyle");

            if (style == 1) return "MBR";
            if (style == 2) return "GPT";

            return "Unknown";
        }

        // ------------------------------------------------------------ SMART

        /// <summary>
        /// Casa cada leitura SMART com o disco correspondente e preenche
        /// <c>failurePredicted</c>.
        ///
        /// **MEDIDO EM CAMPO (JULIA-LAPTOP, 2 discos).** A versão anterior do protótipo só
        /// atribuía quando havia exatamente um disco E uma leitura. Num notebook com SSD de
        /// sistema e HD de dados — configuração corriqueira — o guard falhava e o campo ficava
        /// null nos DOIS, com o dado SMART em mãos. STO-004 é Crítico: perder a leitura é
        /// perder o achado de maior urgência do produto.
        /// </summary>
        public static void AttachSmart(IList<JObject> disks, IList<PropertyBag> readings)
        {
            if (disks == null || disks.Count == 0 || readings == null || readings.Count == 0) return;

            var values = new Dictionary<string, bool>(StringComparer.Ordinal);

            foreach (var reading in readings)
            {
                var instance = reading.Text("InstanceName");
                var predicted = reading.Flag("PredictFailure");

                if (instance == null || !predicted.HasValue) continue;

                values[instance] = predicted.Value;
            }

            var models = disks.Select(disk => (string)disk["model"]).ToList();

            foreach (var pair in SmartCorrelation.Resolve(models, values))
                disks[pair.Key]["failurePredicted"] = pair.Value;
        }
    }

    /// <summary>
    /// Correlação entre leitura SMART e disco físico, pelo MODELO embutido no
    /// <c>InstanceName</c>.
    ///
    /// O <c>InstanceName</c> tem a forma
    /// <c>SCSI\Disk&amp;Ven_WDC&amp;Prod_WD10SPZX-21Z10T0\5&amp;1ca0da9&amp;0&amp;000000_0</c> e o modelo do
    /// disco é <c>WDC WD10SPZX-21Z10T0</c>. Normalizando os dois para maiúsculas
    /// alfanuméricas, cada PALAVRA do modelo aparece no InstanceName — mas não contíguas,
    /// porque <c>Prod_</c> fica no meio. Por isso o teste é palavra a palavra, nunca substring.
    /// </summary>
    public static class SmartCorrelation
    {
        /// <summary>
        /// Índice do disco → falha prevista, só para os casos inequívocos.
        ///
        /// Ambiguidade nas DUAS direções deixa o campo null: uma leitura que casa com dois
        /// discos, e duas leituras que casam com o mesmo disco. Modelos iguais em duas baias
        /// é configuração real, e apontar falha prevista no disco errado é pior que
        /// Indeterminate — manda trocar o disco saudável e deixa o que está morrendo na
        /// máquina.
        /// </summary>
        public static IDictionary<int, bool> Resolve(
            IList<string> diskModels, IDictionary<string, bool> readings)
        {
            var resolved = new Dictionary<int, bool>();
            if (diskModels == null || readings == null) return resolved;

            var candidates = new Dictionary<int, List<bool>>();

            foreach (var reading in readings)
            {
                var matches = new List<int>();

                for (var index = 0; index < diskModels.Count; index++)
                    if (Matches(reading.Key, diskModels[index])) matches.Add(index);

                // Uma leitura que casa com mais de um disco não identifica nenhum.
                if (matches.Count != 1) continue;

                List<bool> list;
                if (!candidates.TryGetValue(matches[0], out list))
                {
                    list = new List<bool>();
                    candidates[matches[0]] = list;
                }

                list.Add(reading.Value);
            }

            foreach (var candidate in candidates)
                if (candidate.Value.Count == 1) resolved[candidate.Key] = candidate.Value[0];

            return resolved;
        }

        public static bool Matches(string instanceName, string model)
        {
            if (string.IsNullOrWhiteSpace(instanceName) || string.IsNullOrWhiteSpace(model)) return false;

            var haystack = Normalize(instanceName);
            if (haystack.Length == 0) return false;

            var words = model.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return false;

            foreach (var word in words)
            {
                var needle = Normalize(word);

                // Palavra curta demais casaria por acaso: "WD" aparece em qualquer InstanceName
                // de disco Western Digital, inclusive no do outro disco da mesma marca.
                if (needle.Length < 3 || haystack.IndexOf(needle, StringComparison.Ordinal) < 0) return false;
            }

            return true;
        }

        private static string Normalize(string value)
        {
            return value == null
                ? string.Empty
                : Regex.Replace(value.ToUpperInvariant(), "[^A-Z0-9]", string.Empty);
        }
    }

    /// <summary>Leitura de TRIM a partir da saída do <c>fsutil</c>.</summary>
    public static class TrimQuery
    {
        // A saída é LOCALIZADA, então o padrão ancora no NÚMERO e nunca na prosa. Em pt-BR a
        // linha vem como "NTFS DisableDeleteNotify = 0  (Permite que operações TRIM ...)".
        private static readonly Regex ComSistemaDeArquivos = new Regex(
            @"^\s*NTFS\s+DisableDeleteNotify\s*=\s*(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

        // Windows mais antigo devolve só "DisableDeleteNotify = 0", sem o prefixo do sistema
        // de arquivos. ReFS tem linha própria e é ignorada de propósito.
        private static readonly Regex SemSistemaDeArquivos = new Regex(
            @"^\s*DisableDeleteNotify\s*=\s*(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

        /// <summary>
        /// <c>0 = notificação de exclusão HABILITADA</c>, ou seja, TRIM ligado. A polaridade é
        /// invertida no nome da chave — ler como "TRIM desabilitado?" leva a erro.
        ///
        /// A sonda confirmou que <c>behavior query</c> responde SEM elevação; só
        /// <c>behavior set</c> exige.
        /// </summary>
        public static bool? Parse(string output)
        {
            if (string.IsNullOrWhiteSpace(output)) return null;

            var match = ComSistemaDeArquivos.Match(output);
            if (!match.Success) match = SemSistemaDeArquivos.Match(output);
            if (!match.Success) return null;

            int value;
            if (!int.TryParse(match.Groups[1].Value, out value)) return null;

            return value == 0;
        }
    }
}
