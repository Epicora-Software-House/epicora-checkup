using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EpicoraCheckup.App.Demo;
using EpicoraCheckup.Core.Contracts;
using EpicoraCheckup.Core.Orchestration;
using EpicoraCheckup.Rules;

namespace EpicoraCheckup.App.Screens
{
    /// <summary>
    /// Tela 2 — coleta em andamento.
    ///
    /// **O requisito é que a janela nunca congele.** A coleta roda fora da thread da UI, e o
    /// progresso volta por <see cref="Progress{T}"/>, que captura o
    /// <see cref="SynchronizationContext"/> da thread que o criou e marshaliza cada
    /// notificação de volta para ela. É o que evita a exceção intermitente e difícil de
    /// reproduzir que o doc 02 §3.3 avisa — nenhum controle é tocado da thread de trabalho.
    ///
    /// **Nenhuma etapa que falhe interrompe a coleta.** Isso é garantido pelo orquestrador,
    /// não por esta tela; aqui só se mostra o resultado.
    /// </summary>
    internal sealed class Screen2Collection : ScreenBase
    {
        private readonly Dictionary<string, StepRow> _rows = new Dictionary<string, StepRow>(StringComparer.Ordinal);
        private readonly Panel _lista = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.FundoCartao };
        private readonly Label _rodapeEstado = new Label();
        private readonly System.Windows.Forms.Timer _cronometro =
            new System.Windows.Forms.Timer { Interval = 100 };

        private bool _started;
        private bool _finished;
        private string _failure;

        internal Screen2Collection(SessionState session) : base(session)
        {
            Build();
        }

        internal override string Title => Strings.Tela2Titulo;

        // Voltar durante ou depois da coleta não faz sentido: a coleta já aconteceu.
        internal override bool CanGoBack => false;

        internal override bool CanAdvance => _finished;

        internal override string BlockedReason => _failure;

        internal override void OnEnter()
        {
            if (_started) return;
            _started = true;

            StartCollection();
        }

        private void Build()
        {
            var explicacao = new Label
            {
                Dock = DockStyle.Top,
                Height = 46,
                Font = Theme.Rotulo,
                ForeColor = Theme.TextoSecundario,
                Text = Strings.Tela2NuncaInterrompe
            };

            _rodapeEstado.Dock = DockStyle.Bottom;
            _rodapeEstado.Height = 30;
            _rodapeEstado.Font = Theme.CorpoNegrito;
            _rodapeEstado.ForeColor = Theme.Texto;
            _rodapeEstado.TextAlign = ContentAlignment.MiddleLeft;

            _cronometro.Tick += (s, e) =>
                _rodapeEstado.Text = string.Format(Strings.Tela2Decorrido, Session.Elapsed.TotalSeconds);

            Controls.Add(_lista);
            Controls.Add(_rodapeEstado);
            Controls.Add(explicacao);
        }

        // ------------------------------------------------------------ execução

        private async void StartCollection()
        {
            try
            {
                var conjunto = BuildCollectors();

                if (conjunto == null || conjunto.Count == 0)
                {
                    ShowNoCollectors();
                    return;
                }

                foreach (var collector in conjunto)
                    AddRow(collector);

                Session.StartedAt = DateTimeOffset.Now;
                _cronometro.Start();

                var orquestrador = new CollectionOrchestrator(conjunto);

                // Progress<T> criado AQUI, na thread da UI: é o que garante que cada Report
                // volte marshalizado. Criá-lo na thread de trabalho quebraria isso em silêncio.
                var progresso = new Progress<CollectionProgress>(Apply);

                var contexto = new CollectionContext
                {
                    IsElevated = Session.IsElevated,
                    OutputDirectory = Session.OutputDirectory,
                    DiagnosticId = Session.Identification.DiagnosticId
                };

                var resultados = await orquestrador
                    .RunAsync(contexto, progresso, CancellationToken.None)
                    .ConfigureAwait(true);

                Session.CollectorResults = resultados;
                Session.FinishedAt = DateTimeOffset.Now;

                Evaluate(resultados);

                _cronometro.Stop();
                _rodapeEstado.Text = string.Format(Strings.Tela2Concluida, Session.Elapsed.TotalSeconds);

                _finished = true;
                _failure = null;
                RaiseStateChanged();
            }
            catch (Exception ex)
            {
                _cronometro.Stop();
                _failure = ex.Message;
                _rodapeEstado.ForeColor = Theme.Alto;
                _rodapeEstado.Text = ex.Message;
                RaiseStateChanged();
            }
        }

        /// <summary>
        /// Avalia a matriz sobre os RESULTADOS dos coletores, não sobre o arquivo de onde eles
        /// porventura vieram. No modo demonstração as duas coisas seriam parecidas, e o atalho
        /// faria a demonstração exercitar um caminho que produção não usa.
        /// </summary>
        private void Evaluate(IList<CollectorResult> resultados)
        {
            var rules = RuleRepository.LoadFromDirectory(RulesLocator.Find());
            var documento = CollectionDocumentBuilder.FromResults(resultados);

            var avaliacao = new RuleEngine(rules).Evaluate(documento);

            Session.Findings = avaliacao.Result.Findings;
            Session.Score = avaliacao.Result.Score;
        }

        private IReadOnlyList<ICollector> BuildCollectors()
        {
            if (!Session.IsDemo) return null;

            return FixtureCollector.Load(Session.DemoFixturePath);
        }

        private void ShowNoCollectors()
        {
            _cronometro.Stop();
            _failure = "coletores não implementados";

            _lista.Controls.Add(new Label
            {
                Dock = DockStyle.Top,
                Height = 140,
                Padding = new Padding(16),
                Font = Theme.Corpo,
                ForeColor = Theme.Texto,
                Text = Strings.ColetoresNaoPortados
            });

            RaiseStateChanged();
        }

        // ------------------------------------------------------------ linhas

        private void AddRow(ICollector collector)
        {
            var row = new StepRow(collector.DisplayName);

            // Ordem de execução visível de cima para baixo.
            Stack(_lista, row);

            _rows[collector.Id] = row;
        }

        private void Apply(CollectionProgress progress)
        {
            StepRow row;
            if (!_rows.TryGetValue(progress.CollectorId, out row)) return;

            row.Update(progress);

            // Mantém a etapa corrente visível numa lista de dezesseis itens.
            if (progress.Phase == CollectorPhase.Running) _lista.ScrollControlIntoView(row);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _cronometro.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>Uma etapa na lista: nome, estado e resumo de uma linha.</summary>
        private sealed class StepRow : Panel
        {
            private readonly Label _nome = new Label();
            private readonly Label _estado = new Label();
            private readonly Label _detalhe = new Label();

            internal StepRow(string displayName)
            {
                Dock = DockStyle.Top;
                Height = 40;
                BackColor = Theme.FundoCartao;

                _nome.Left = 14;
                _nome.Top = 4;
                _nome.Width = 250;
                _nome.Height = 18;
                _nome.Font = Theme.CorpoNegrito;
                _nome.ForeColor = Theme.Texto;
                _nome.Text = displayName;

                _estado.Left = 14;
                _estado.Top = 21;
                _estado.Width = 110;
                _estado.Height = 16;
                _estado.Font = Theme.Rotulo;
                _estado.ForeColor = Theme.TextoSecundario;
                _estado.Text = Strings.EtapaPendente;

                _detalhe.Left = 128;
                _detalhe.Top = 21;
                _detalhe.Width = 700;
                _detalhe.Height = 16;
                _detalhe.Font = Theme.Rotulo;
                _detalhe.ForeColor = Theme.TextoSecundario;
                _detalhe.AutoEllipsis = true;

                Controls.Add(_nome);
                Controls.Add(_estado);
                Controls.Add(_detalhe);

                var separador = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Fundo };
                Controls.Add(separador);
            }

            internal void Update(CollectionProgress progress)
            {
                _estado.Text = Theme.NomeDaFase(progress.Phase);
                _estado.ForeColor = Theme.CorDaFase(progress.Phase);

                switch (progress.Phase)
                {
                    case CollectorPhase.Completed:
                        _detalhe.Text = progress.Summary ?? string.Empty;
                        _detalhe.ForeColor = Theme.TextoSecundario;
                        break;

                    case CollectorPhase.Skipped:
                        _detalhe.Text = progress.Detail ?? Strings.Tela2SemPrivilegio;
                        _detalhe.ForeColor = Theme.Indeterminado;
                        break;

                    case CollectorPhase.Failed:
                        _detalhe.Text = progress.TimedOut
                            ? Strings.Tela2TempoLimite
                            : progress.Detail ?? string.Empty;
                        _detalhe.ForeColor = Theme.Alto;
                        break;

                    default:
                        _detalhe.Text = string.Empty;
                        break;
                }
            }
        }
    }
}
