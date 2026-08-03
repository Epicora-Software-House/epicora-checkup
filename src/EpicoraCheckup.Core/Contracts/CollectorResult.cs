using System;
using System.Collections.Generic;
using EpicoraCheckup.Core.Model;

namespace EpicoraCheckup.Core.Contracts
{
    /// <summary>Erro não fatal ocorrido dentro de um coletor que ainda assim concluiu.</summary>
    public sealed class CollectorError
    {
        public string Source { get; set; }

        public string Message { get; set; }

        /// <summary>
        /// Só para o log e o pacote de entrega interna, nunca para a tela do cliente.
        /// </summary>
        public string Detail { get; set; }
    }

    /// <summary>
    /// O que um coletor devolve (doc 02 §3.1): estado, payload, resumo de uma linha
    /// para a tela 2, e os erros não fatais.
    /// </summary>
    public sealed class CollectorResult
    {
        public string Id { get; set; }

        public string DisplayName { get; set; }

        public CollectorStatus Status { get; set; }

        /// <summary>
        /// Por que foi ignorado, quando <see cref="CollectorStatus.Skipped"/>. Este texto
        /// chega ao relatório dentro do bloco "não foi possível verificar", então é
        /// escrito para quem lê o relatório, não para quem depura o código.
        /// </summary>
        public string SkipReason { get; set; }

        public bool RequiresElevation { get; set; }

        public long DurationMs { get; set; }

        /// <summary>
        /// Se o coletor foi interrompido por tempo limite. Registrado separadamente de
        /// <see cref="CollectorStatus.Failed"/> porque WMI travado em repositório
        /// corrompido é justamente o tipo de máquina que a Epicora vai encontrar, e
        /// distinguir "travou" de "deu erro" muda a conversa com o cliente.
        /// </summary>
        public bool TimedOut { get; set; }

        /// <summary>Resumo de uma linha exibido na tela 2: "14 programas na inicialização".</summary>
        public string Summary { get; set; }

        public IList<CollectorError> Errors { get; set; }

        /// <summary>
        /// Payload específico do coletor. <c>object</c> por opção: Core não referencia
        /// biblioteca de JSON, e a forma de cada payload é definida pelo schema, não
        /// por um tipo aqui.
        ///
        /// Campo ausente é <c>null</c> com o motivo registrado em <see cref="Errors"/>.
        /// Nunca zero, nunca string vazia, nunca "N/A" — isso destrói a análise no
        /// consolidador (doc 02 §5).
        /// </summary>
        public object Data { get; set; }

        public static CollectorResult Skipped(ICollector collector, string reason)
        {
            if (collector == null) throw new ArgumentNullException(nameof(collector));

            return new CollectorResult
            {
                Id = collector.Id,
                DisplayName = collector.DisplayName,
                Status = CollectorStatus.Skipped,
                SkipReason = reason,
                RequiresElevation = collector.RequiresElevation,
                Errors = new List<CollectorError>()
            };
        }
    }
}
