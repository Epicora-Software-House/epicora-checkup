using System.Collections.Generic;
using EpicoraCheckup.Collectors.Collectors;
using EpicoraCheckup.Core.Contracts;

namespace EpicoraCheckup.Collectors
{
    /// <summary>
    /// Os dezesseis coletores, na ordem em que rodam.
    ///
    /// A ordem é a mesma do protótipo e é a que o técnico vê na tela 2. Não é arbitrária:
    /// começa pelo que identifica a máquina — se o cliente interromper a coleta no meio, o que
    /// já está na tela diz DE QUE máquina se está falando — e termina pelo que é lento ou
    /// ainda não avalia nada.
    ///
    /// O documento funcional §5 lista quinze etapas; aqui são dezesseis porque <c>cpu</c> e
    /// <c>memory</c> estão separados: são domínios com falha independente, e o tempo limite de
    /// um não deve derrubar o outro (schema/campo-para-decisao.md).
    /// </summary>
    public static class WindowsCollectorSet
    {
        public static IReadOnlyList<ICollector> Create()
        {
            return new List<ICollector>
            {
                new MachineCollector(),
                new CpuCollector(),
                new MemoryCollector(),
                new StorageCollector(),
                new DevicesCollector(),
                new OsCollector(),
                new UpdatesCollector(),
                new Win11Collector(),
                new SecurityCollector(),
                new AntivirusCollector(),
                new SoftwareCollector(),
                new StartupCollector(),
                new NetworkCollector(),
                new AccountsCollector(),
                new BatteryCollector(),
                new EventsCollector()
            };
        }
    }
}
