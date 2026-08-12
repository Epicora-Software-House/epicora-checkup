using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using EpicoraCheckup.Collectors.Sources;
using EpicoraCheckup.Core.Contracts;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Collectors.Collectors
{
    /// <summary>
    /// Programas de inicialização.
    ///
    /// <c>coverageIsPartial</c> é <c>true</c>: as chaves <c>Run</c> e as pastas de
    /// inicialização não cobrem tarefas agendadas nem mecanismos modernos. Aceitável para
    /// inventário; **insuficiente para a Fase 5** — desativar item que a ferramenta não
    /// enxerga inteiro é como se decide desligar o que não devia.
    /// </summary>
    public sealed class StartupCollector : CollectorBase
    {
        private static readonly RunKey[] RunKeys =
        {
            new RunKey(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM-Run"),
            new RunKey(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM-RunOnce"),
            new RunKey(RegistryHive.LocalMachine,
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM-Run"),
            new RunKey(RegistryHive.CurrentUser,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKCU-Run"),
            new RunKey(RegistryHive.CurrentUser,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU-RunOnce")
        };

        public override string Id
        {
            get { return "startup"; }
        }

        public override string DisplayName
        {
            get { return "Programas de inicialização"; }
        }

        public override int EstimatedSeconds
        {
            get { return 7; }
        }

        protected override JObject Read(
            CollectionContext context, ErrorSink errors, CancellationToken cancellationToken)
        {
            var items = new List<JObject>();

            foreach (var key in RunKeys)
            {
                foreach (var value in RegistryReader.Values(key.Hive, key.Path))
                    items.Add(Item(value.Key, StartupFacts.CommandText(value.Value), key.Label));
            }

            foreach (var folder in new[]
            {
                new { Path = Environment.GetFolderPath(Environment.SpecialFolder.Startup), Label = "StartupFolder" },
                new { Path = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Label = "CommonStartupFolder" }
            })
            {
                if (string.IsNullOrEmpty(folder.Path) || !Directory.Exists(folder.Path)) continue;

                var files = errors.Read(folder.Label, () => Directory.GetFiles(folder.Path));
                if (files == null) continue;

                foreach (var file in files)
                    items.Add(Item(Path.GetFileNameWithoutExtension(file), file, folder.Label));
            }

            // Assinatura e fabricante alimentam a lista de exclusão da Fase 5: é o que impede
            // desativar o agente de backup ou o driver do leitor fiscal (ADR-007).
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Sign(item);
            }

            var data = new JObject();

            data["count"] = items.Count;
            data["coverageIsPartial"] = true;
            data["items"] = Payload.ArrayOrNull(items);
            data["scheduledLogonTaskCount"] = errors.Read("MSFT_ScheduledTask", CountLogonTasks);

            return Payload.Sanitized(data);
        }

        protected override string Summarize(JObject data)
        {
            return data["count"] + " programas na inicialização";
        }

        private static JObject Item(string name, string command, string location)
        {
            var item = new JObject();

            item["name"] = name;
            item["command"] = command;
            item["location"] = location;
            item["publisher"] = null;
            item["signed"] = null;
            item["enabled"] = null;

            // Preenchidos pela Fase 5 a partir de rules/startup-exclusions.json. Nulo aqui
            // significa "ainda não avaliado", não "pode desativar".
            item["protected"] = null;
            item["protectionReason"] = null;

            return item;
        }

        private static void Sign(JObject item)
        {
            var executable = StartupFacts.ExecutablePath((string)item["command"]);
            if (executable == null) return;

            var signature = Authenticode.Check(executable);

            item["signed"] = signature.Valid;
            item["publisher"] = signature.Publisher;
        }

        /// <summary>
        /// Tarefas agendadas que disparam no logon e não estão desativadas.
        ///
        /// Só a CONTAGEM entra no inventário: a lista completa de tarefas agendadas de uma
        /// máquina é grande, quase toda do próprio Windows, e não sustenta achado nenhum.
        /// </summary>
        private static int CountLogonTasks()
        {
            var tasks = Wmi.Instances(Wmi.TaskScheduler, "MSFT_ScheduledTask");

            return tasks.Count(task =>
                task.Int("State") != StartupFacts.TaskStateDisabled &&
                task.Embedded("Triggers").Any(trigger =>
                    string.Equals(trigger.ClassName, "MSFT_TaskLogonTrigger", StringComparison.Ordinal)));
        }

        private sealed class RunKey
        {
            public RunKey(RegistryHive hive, string path, string label)
            {
                Hive = hive;
                Path = path;
                Label = label;
            }

            public RegistryHive Hive { get; }

            public string Path { get; }

            public string Label { get; }
        }
    }

    /// <summary>Derivação pura do coletor de inicialização.</summary>
    public static class StartupFacts
    {
        /// <summary>TASK_STATE_DISABLED. Tarefa desativada não roda no logon.</summary>
        public const int TaskStateDisabled = 1;

        private static readonly Regex Quoted = new Regex(@"^\s*""([^""]+)""", RegexOptions.CultureInvariant);

        private static readonly Regex Bare = new Regex(@"^\s*(\S+\.exe)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Valor do registro como texto, inclusive quando vem como vetor de linhas.</summary>
        public static string CommandText(object value)
        {
            if (value == null) return null;

            var lines = value as string[];
            if (lines != null) return string.Join(" ", lines);

            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Executável de dentro da linha de comando, com variáveis de ambiente expandidas, ou
        /// <c>null</c> quando não dá para isolar o caminho.
        ///
        /// Caminho sem aspas e COM espaço é ambíguo por natureza — <c>C:\Program Files\x y.exe</c>
        /// sem aspas pode ser executável ou executável mais argumento. Nesse caso o campo
        /// <c>signed</c> fica null, que é honesto; adivinhar aqui atribuiria a assinatura de um
        /// binário a outro.
        /// </summary>
        public static string ExecutablePath(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;

            var match = Quoted.Match(command);
            if (!match.Success) match = Bare.Match(command);
            if (!match.Success) return null;

            try
            {
                var path = Environment.ExpandEnvironmentVariables(match.Groups[1].Value);
                return File.Exists(path) ? path : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
