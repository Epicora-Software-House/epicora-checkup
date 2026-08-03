using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EpicoraCheckup.Core.Contracts;
using EpicoraCheckup.Core.Model;

namespace EpicoraCheckup.Core.Orchestration
{
    /// <summary>
    /// Roda os coletores em sequência (doc 02 §3.2).
    ///
    /// Duas garantias, e a primeira é o requisito de robustez número um do projeto:
    ///
    ///  1. **Falha de um coletor nunca aborta os outros.** Cada chamada tem try/catch
    ///     próprio, e o resultado é sempre uma lista com um item por coletor — nenhum
    ///     desaparece por ter dado erro.
    ///  2. **Tempo limite por coletor.** WMI pode travar indefinidamente em máquina com
    ///     repositório corrompido, que é justamente o tipo de máquina que a Epicora vai
    ///     encontrar. Sem limite, a ferramenta fica pendurada na frente do cliente.
    ///
    /// Não referencia WinForms e não sabe que existe UI: reporta progresso por
    /// <see cref="IProgress{T}"/>, e quem hospeda decide como mostrar.
    /// </summary>
    public sealed class CollectionOrchestrator
    {
        private readonly IReadOnlyList<ICollector> _collectors;

        public CollectionOrchestrator(IReadOnlyList<ICollector> collectors)
        {
            _collectors = collectors ?? throw new ArgumentNullException(nameof(collectors));
        }

        public IReadOnlyList<ICollector> Collectors => _collectors;

        public async Task<IList<CollectorResult>> RunAsync(
            CollectionContext context,
            IProgress<CollectionProgress> progress,
            CancellationToken cancellationToken)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var results = new List<CollectorResult>(_collectors.Count);

            for (var index = 0; index < _collectors.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var collector = _collectors[index];

                Report(progress, collector, index, _collectors.Count, CollectorPhase.Running, null, null, 0, false);

                var result = await RunOneAsync(collector, context, cancellationToken).ConfigureAwait(false);
                results.Add(result);

                Report(progress, collector, index, _collectors.Count, PhaseOf(result.Status), result.Summary,
                    DetailOf(result), result.DurationMs, result.TimedOut);
            }

            return results;
        }

        private async Task<CollectorResult> RunOneAsync(
            ICollector collector,
            CollectionContext context,
            CancellationToken cancellationToken)
        {
            // Sem privilégio, coletor que exige privilégio é IGNORADO — nunca falhado, e
            // nunca achado negativo. O relatório sai parcial e honesto.
            if (collector.RequiresElevation && !context.IsElevated)
                return CollectorResult.Skipped(collector, "sem privilégio de administrador");

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var result = await RunWithTimeoutAsync(collector, context, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();

                if (result == null)
                {
                    // Coletor que devolve null é bug do coletor, não da máquina. Registrado
                    // como falha em vez de virar NullReferenceException três telas depois.
                    return Failure(collector, stopwatch.ElapsedMilliseconds, false,
                        "o coletor não devolveu resultado");
                }

                // Normaliza o que é responsabilidade do orquestrador, e não do coletor:
                // identidade e duração medida de fora.
                result.Id = collector.Id;
                result.DisplayName = collector.DisplayName;
                result.RequiresElevation = collector.RequiresElevation;
                result.DurationMs = stopwatch.ElapsedMilliseconds;
                if (result.Errors == null) result.Errors = new List<CollectorError>();

                return result;
            }
            catch (TimeoutException)
            {
                stopwatch.Stop();
                return Failure(collector, stopwatch.ElapsedMilliseconds, true, "tempo limite excedido");
            }
            catch (OperationCanceledException)
            {
                // Cancelamento é decisão de quem opera, não erro. Sobe.
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return Failure(collector, stopwatch.ElapsedMilliseconds, false, ex.Message, ex.ToString());
            }
        }

        /// <summary>
        /// Roda o coletor com tempo limite.
        ///
        /// Confiança M declarada no doc 02 §3.2: cancelar uma chamada WMI síncrona em
        /// andamento não é trivial em .NET. O que este método garante é que a FERRAMENTA
        /// segue adiante; a thread do coletor travado pode ficar órfã até o processo
        /// terminar. É custo aceito e consciente — o inaceitável é a janela congelada na
        /// frente do cliente.
        ///
        /// A exceção de uma tarefa órfã é observada no descarte para não virar
        /// UnobservedTaskException depois, quando o contexto já não diz nada.
        /// </summary>
        private static async Task<CollectorResult> RunWithTimeoutAsync(
            ICollector collector,
            CollectionContext context,
            CancellationToken cancellationToken)
        {
            using (var perCollector = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var work = Task.Run(() => collector.Collect(context, perCollector.Token), perCollector.Token);
                var timeout = Task.Delay(context.CollectorTimeout, perCollector.Token);

                var first = await Task.WhenAny(work, timeout).ConfigureAwait(false);

                if (first == work)
                {
                    // Cancela para encerrar o Task.Delay que ficou pendente. Sem isto cada
                    // coletor deixa um timer vivo até expirar — dezesseis coletores, dezesseis
                    // timers de 20 segundos à toa.
                    perCollector.Cancel();

                    // Propaga exceção do coletor para o try/catch de quem chamou.
                    return await work.ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Pede cancelamento por educação — o coletor pode estar num ponto que
                // observa o token — e abandona.
                //
                // O CancellationTokenSource é descartado ao sair do using, enquanto a tarefa
                // órfã ainda segura o token dele. Ler IsCancellationRequested de um token de
                // fonte descartada é seguro; registrar callback novo lança, e é justamente
                // por isso que Abandon observa a exceção da órfã.
                perCollector.Cancel();
                Abandon(work);

                throw new TimeoutException($"coletor \"{collector.Id}\" excedeu {context.CollectorTimeout.TotalSeconds:0} segundos");
            }
        }

        private static void Abandon(Task task)
        {
            task.ContinueWith(
                t => { var _ = t.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static CollectorResult Failure(
            ICollector collector,
            long durationMs,
            bool timedOut,
            string message,
            string detail = null)
        {
            return new CollectorResult
            {
                Id = collector.Id,
                DisplayName = collector.DisplayName,
                Status = CollectorStatus.Failed,
                RequiresElevation = collector.RequiresElevation,
                DurationMs = durationMs,
                TimedOut = timedOut,
                Summary = null,
                Data = null,
                Errors = new List<CollectorError>
                {
                    new CollectorError { Source = collector.Id, Message = message, Detail = detail }
                }
            };
        }

        private static CollectorPhase PhaseOf(CollectorStatus status)
        {
            switch (status)
            {
                case CollectorStatus.Completed: return CollectorPhase.Completed;
                case CollectorStatus.Skipped: return CollectorPhase.Skipped;
                default: return CollectorPhase.Failed;
            }
        }

        private static string DetailOf(CollectorResult result)
        {
            if (result.Status == CollectorStatus.Skipped) return result.SkipReason;
            if (result.Errors != null && result.Errors.Count > 0) return result.Errors[0].Message;
            return null;
        }

        private static void Report(
            IProgress<CollectionProgress> progress,
            ICollector collector,
            int index,
            int total,
            CollectorPhase phase,
            string summary,
            string detail,
            long durationMs,
            bool timedOut)
        {
            progress?.Report(new CollectionProgress
            {
                CollectorId = collector.Id,
                DisplayName = collector.DisplayName,
                Index = index,
                Total = total,
                Phase = phase,
                Summary = summary,
                Detail = detail,
                DurationMs = durationMs,
                TimedOut = timedOut
            });
        }
    }
}
