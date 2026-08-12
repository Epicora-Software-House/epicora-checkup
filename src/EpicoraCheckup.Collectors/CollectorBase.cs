using System;
using System.Collections.Generic;
using System.Threading;
using EpicoraCheckup.Core.Contracts;
using EpicoraCheckup.Core.Model;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Collectors
{
    /// <summary>
    /// Base dos coletores: o que é igual para os dezesseis fica aqui, e cada um implementa
    /// só a leitura do seu domínio.
    ///
    /// Espelha <c>Invoke-Collector</c> do protótipo, inclusive na regra dura: **nenhuma falha
    /// de coletor interrompe a coleta.** O que este tipo acrescenta ao que o orquestrador já
    /// garante é o resultado PARCIAL — sub-leitura que falha degrada um campo para null e
    /// registra o motivo, em vez de derrubar o coletor inteiro e perder o que já foi lido.
    /// </summary>
    public abstract class CollectorBase : ICollector
    {
        public abstract string Id { get; }

        public abstract string DisplayName { get; }

        /// <summary>
        /// Padrão <c>false</c>, e isso é decisão medida em campo, não descuido.
        ///
        /// A sonda mostrou que só TPM, BitLocker e SMART exigem privilégio, e as três degradam
        /// para null isoladamente. Coletor inteiro marcado <c>true</c> descarta de graça, em
        /// toda visita sem senha de administrador, a família de achados de maior valor
        /// comercial do produto — foi o que aconteceu com SEC-001/002/003, SEC-006, SEC-008 e
        /// SEC-009 antes da correção.
        /// </summary>
        public virtual bool RequiresElevation
        {
            get { return false; }
        }

        public abstract int EstimatedSeconds { get; }

        public CollectorResult Collect(CollectionContext context, CancellationToken cancellationToken)
        {
            var skipReason = SkipReason(context);
            if (skipReason != null)
            {
                var skipped = CollectorResult.Skipped(this, skipReason);
                skipped.Summary = "Ignorado — " + skipReason;
                return skipped;
            }

            var errors = new ErrorSink();
            var data = Read(context, errors, cancellationToken);

            return new CollectorResult
            {
                Id = Id,
                DisplayName = DisplayName,
                Status = CollectorStatus.Completed,
                RequiresElevation = RequiresElevation,
                Summary = SafeSummary(data),
                Errors = errors.Errors,

                // Rede de segurança: cada coletor já devolve o payload limpo, mas quem escrever
                // o próximo não vai lembrar disso, e o modo de falhar é silencioso — campo nulo
                // que o motor enxerga como preenchido. Ver Payload.Sanitized.
                Data = Payload.Sanitized(data)
            };
        }

        /// <summary>
        /// Motivo para ignorar o coletor nesta máquina, ou <c>null</c> para executar. É o
        /// <c>NotApplicable</c> do protótipo — bateria em desktop, e mais nada por enquanto.
        ///
        /// Elevação NÃO se resolve aqui: quem trata isso é o orquestrador, antes de chamar.
        /// </summary>
        protected virtual string SkipReason(CollectionContext context)
        {
            return null;
        }

        protected abstract JObject Read(
            CollectionContext context, ErrorSink errors, CancellationToken cancellationToken);

        /// <summary>Resumo de uma linha para a tela 2, em linguagem de cliente.</summary>
        protected abstract string Summarize(JObject data);

        /// <summary>
        /// Defeito no resumo não pode custar a coleta.
        ///
        /// O resumo é texto de tela; o payload é o produto. Deixar uma exceção de formatação
        /// subir daqui marcaria o coletor como Failed e jogaria fora dado já colhido — o pior
        /// negócio possível na frente do cliente.
        /// </summary>
        private string SafeSummary(JObject data)
        {
            try
            {
                return data == null ? null : Summarize(data);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ------------------------------------------------------------ auxiliares de leitura

        protected static string TextOf(JToken token)
        {
            return token == null || token.Type == JTokenType.Null ? null : (string)token;
        }

        protected static bool? FlagOf(JToken token)
        {
            return token == null || token.Type == JTokenType.Null ? null : (bool?)token;
        }

        protected static long? LongOf(JToken token)
        {
            return token == null || token.Type == JTokenType.Null ? null : (long?)token;
        }

        protected static int CountOf(JToken token)
        {
            var array = token as JArray;
            return array == null ? 0 : array.Count;
        }

        /// <summary>
        /// Formata bytes para o resumo da tela 2. Só apresentação — o JSON guarda bytes
        /// inteiros sempre (doc 02, <c>$defs/bytes</c>).
        /// </summary>
        protected static string FormatBytes(long? bytes)
        {
            if (!bytes.HasValue) return "desconhecido";

            var gb = Math.Round(bytes.Value / 1073741824d, 1);
            return gb.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + " GB";
        }

        protected static IList<T> Safe<T>(IList<T> list)
        {
            return list ?? new List<T>();
        }
    }
}
