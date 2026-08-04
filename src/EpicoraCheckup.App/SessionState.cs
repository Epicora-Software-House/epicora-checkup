using System;
using System.Collections.Generic;
using System.IO;
using EpicoraCheckup.Core.Contracts;
using EpicoraCheckup.Core.Model;
using EpicoraCheckup.Reporting;
using Newtonsoft.Json;

namespace EpicoraCheckup.App
{
    /// <summary>
    /// Estado da execução, carregado entre as telas.
    ///
    /// Uma instância por execução da ferramenta. Não é singleton estático: a tela recebe a
    /// instância, o que torna possível instanciar uma tela em teste sem montar estado global.
    ///
    /// <see cref="Identification"/> e <see cref="ManualData"/> vivem em Core porque Reporting
    /// também precisa deles, e Reporting não referencia WinForms.
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

        /// <summary>
        /// Log da execução. Acumula em memória e é gravado no fim, junto com o relatório —
        /// o nome do arquivo depende do hostname, que só se conhece depois de coletar.
        /// </summary>
        public RunLog Log { get; } = new RunLog();

        /// <summary>Arquivos efetivamente gravados, para a tela 7. Vazio em demonstração.</summary>
        public IList<string> GeneratedFiles { get; } = new List<string>();

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
                Identification.CorporateEnvironment = saved.CorporateEnvironment;
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
