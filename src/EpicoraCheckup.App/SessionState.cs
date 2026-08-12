using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using EpicoraCheckup.Core.Contracts;
using EpicoraCheckup.Core.Model;
using EpicoraCheckup.Reporting;
using Newtonsoft.Json;

namespace EpicoraCheckup.App
{
    /// <summary>Campos da tela 1. Persistidos entre máquinas da mesma visita.</summary>
    public sealed class Identification
    {
        public string Technician { get; set; }
        public string Client { get; set; }
        public string Unit { get; set; }
        public string DiagnosticId { get; set; }

        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(Technician) &&
            !string.IsNullOrWhiteSpace(Client) &&
            !string.IsNullOrWhiteSpace(DiagnosticId);
    }

    /// <summary>Campos da tela 4. Os três primeiros são obrigatórios (doc 01 §5).</summary>
    public sealed class ManualData
    {
        public string MachineLabel { get; set; }
        public string Responsible { get; set; }
        public string Department { get; set; }
        public string PhysicalLocation { get; set; }
        public string AssetTag { get; set; }
        public string PhysicalCondition { get; set; }
        public string Notes { get; set; }

        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(MachineLabel) &&
            !string.IsNullOrWhiteSpace(Responsible) &&
            !string.IsNullOrWhiteSpace(Department);
    }

    /// <summary>
    /// Estado da execução, carregado entre as telas.
    ///
    /// Uma instância por execução da ferramenta. Não é singleton estático: a tela recebe a
    /// instância, o que torna possível instanciar uma tela em teste sem montar estado global.
    /// </summary>
    public sealed class SessionState
    {
        public Identification Identification { get; } = new Identification();

        public ManualData Manual { get; } = new ManualData();

        public bool IsElevated { get; set; }

        /// <summary>
        /// Modo demonstração: os dados vêm de uma fixture e NENHUM arquivo é gravado.
        /// Ver <see cref="Demo.FixtureCollector"/> para por que não gravar é parte do desenho.
        /// </summary>
        public bool IsDemo { get; set; }

        public string DemoFixturePath { get; set; }

        public string OutputDirectory { get; set; }

        public DateTimeOffset StartedAt { get; set; }

        public DateTimeOffset? FinishedAt { get; set; }

        public IList<CollectorResult> CollectorResults { get; set; } = new List<CollectorResult>();

        public IList<Finding> Findings { get; set; } = new List<Finding>();

        public Score Score { get; set; }

        /// <summary>Arquivos efetivamente gravados, para a tela 7. Vazio em demonstração.</summary>
        public IList<string> GeneratedFiles { get; } = new List<string>();

        /// <summary>
        /// Pasta onde os arquivos desta máquina foram gravados — <c>&lt;saída&gt;\&lt;CLIENTE&gt;\</c>,
        /// e não a pasta de saída em si. É a que o botão "Abrir pasta" da tela 7 abre.
        /// </summary>
        public string ReportDirectory { get; set; }

        /// <summary>
        /// Converte o estado da sessão no que a gravação precisa.
        ///
        /// A conversão mora aqui, e não em Reporting, porque a direção da dependência é essa:
        /// Reporting não conhece o assistente nem WinForms, e o consolidador da Fase 4 vai
        /// montar o mesmo tipo a partir de arquivo lido do disco.
        /// </summary>
        public CheckupRun ToRun(string toolVersion)
        {
            return new CheckupRun
            {
                ToolVersion = toolVersion,
                StartedAt = StartedAt,
                FinishedAt = FinishedAt ?? DateTimeOffset.Now,
                Elevated = IsElevated,
                Technician = Identification.Technician,
                DiagnosticId = Identification.DiagnosticId,
                HostLocale = CultureInfo.CurrentCulture.Name,
                ClientName = Identification.Client,
                ClientUnit = Identification.Unit,
                MachineLabel = Manual.MachineLabel,
                Responsible = Manual.Responsible,
                Department = Manual.Department,
                PhysicalLocation = Manual.PhysicalLocation,
                AssetTag = Manual.AssetTag,
                PhysicalCondition = Manual.PhysicalCondition,
                Notes = Manual.Notes,

                // Ponto aberto: a tela 4 não tem esta marcação, porque o doc 01 §5 não a lista
                // entre os campos dela. Null significa "não marcado", e OS-004 continua sem
                // base em máquina fora de domínio. Ver src/README.md.
                CorporateEnvironment = null,

                Collectors = CollectorResults,
                Findings = Findings,
                Score = Score
            };
        }

        public TimeSpan Elapsed =>
            (FinishedAt ?? DateTimeOffset.Now) - StartedAt;

        // ------------------------------------------------------------ persistência da tela 1

        /// <summary>
        /// A tela 1 é persistida DENTRO da pasta de saída, não em AppData nem no registro.
        ///
        /// O doc 01 §5 pede que os campos sejam persistidos para não redigitar em cada
        /// máquina da mesma visita. A regra 3 de contribuição proíbe escrita fora da pasta
        /// de saída até a Fase 5. As duas coisas só coexistem aqui.
        /// </summary>
        private string SessionFilePath => Path.Combine(OutputDirectory ?? ".", "epicora-sessao.json");

        public void LoadIdentification()
        {
            try
            {
                if (OutputDirectory == null || !File.Exists(SessionFilePath)) return;

                var saved = JsonConvert.DeserializeObject<Identification>(File.ReadAllText(SessionFilePath));
                if (saved == null) return;

                Identification.Technician = saved.Technician;
                Identification.Client = saved.Client;
                Identification.Unit = saved.Unit;
                Identification.DiagnosticId = saved.DiagnosticId;
            }
            catch (Exception)
            {
                // Conveniência, não requisito. Arquivo corrompido ou ilegível apenas
                // significa que o técnico redigita — nunca impede a ferramenta de abrir.
            }
        }

        public void SaveIdentification()
        {
            // Demonstração não grava nada, em nenhuma circunstância.
            if (IsDemo || OutputDirectory == null) return;

            try
            {
                Directory.CreateDirectory(OutputDirectory);
                File.WriteAllText(SessionFilePath, JsonConvert.SerializeObject(Identification, Formatting.Indented));
            }
            catch (Exception)
            {
                // Idem: falhar em salvar a conveniência não pode interromper o diagnóstico.
            }
        }
    }
}
