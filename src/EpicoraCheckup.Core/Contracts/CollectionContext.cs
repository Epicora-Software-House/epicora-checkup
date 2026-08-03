using System;

namespace EpicoraCheckup.Core.Contracts
{
    /// <summary>
    /// Contexto passado a cada coletor. Montado uma vez no início da execução.
    /// </summary>
    public sealed class CollectionContext
    {
        /// <summary>
        /// Se o processo está elevado, detectado no início e propagado (doc 02 §3.4).
        ///
        /// A ferramenta DEVE rodar sem elevação: o cenário real é o técnico sem a senha
        /// de administrador local na máquina do cliente. Nesse caso o relatório sai
        /// parcial e honesto, não sai errado e não deixa de sair.
        /// </summary>
        public bool IsElevated { get; set; }

        /// <summary>
        /// Tempo máximo por coletor. WMI pode travar indefinidamente em máquina com
        /// repositório corrompido, e sem limite a ferramenta fica pendurada na frente
        /// do cliente.
        /// </summary>
        public TimeSpan CollectorTimeout { get; set; } = TimeSpan.FromSeconds(20);

        /// <summary>Pasta de saída. Nada é escrito fora dela.</summary>
        public string OutputDirectory { get; set; }

        /// <summary>Identificador do diagnóstico, para rastreabilidade.</summary>
        public string DiagnosticId { get; set; }
    }
}
