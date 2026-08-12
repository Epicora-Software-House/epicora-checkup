using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EpicoraCheckup.Core.Contracts;
using EpicoraCheckup.Core.Model;
using EpicoraCheckup.Reporting;
using EpicoraCheckup.Rules;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Reporting.Tests
{
    /// <summary>
    /// Localiza as pastas do repositório e monta uma execução a partir de uma fixture real.
    ///
    /// A fixture é a MESMA que o motor de regras usa nos golden files. É deliberado: o
    /// relatório precisa ser gerado a partir do que a ferramenta produz de verdade, não de um
    /// objeto montado para o teste passar.
    /// </summary>
    internal static class Repositorio
    {
        private static readonly Lazy<string> RaizLazy = new Lazy<string>(() => AcharRaiz());

        internal static string Raiz => RaizLazy.Value;

        internal static string Fixture(string nome)
        {
            return Path.Combine(Raiz, "tests", "fixtures", nome + ".json");
        }

        internal static JObject LerJson(string caminho)
        {
            var texto = File.ReadAllText(caminho);
            if (texto.Length > 0 && texto[0] == '﻿') texto = texto.Substring(1);

            return JObject.Parse(texto);
        }

        /// <summary>
        /// Uma execução completa: coletores da fixture, achados e score do motor de regras de
        /// verdade sobre a matriz de verdade.
        /// </summary>
        /// <param name="matrizCompleta">
        /// Falso reproduz o que a ferramenta produz HOJE — só as 5 regras habilitadas de 61.
        /// Verdadeiro inclui as pendentes, e serve para exercitar as seções do relatório que
        /// hoje sairiam vazias por falta de regra habilitada, não por falta de código.
        /// </param>
        internal static CheckupRun Execucao(string fixture = "sintetica-vermelha", bool matrizCompleta = false)
        {
            var documento = LerJson(Fixture(fixture));
            var coletores = Coletores(documento);

            var regras = RuleRepository.LoadFromDirectory(Path.Combine(Raiz, "rules"));
            var avaliacao = new RuleEngine(regras)
                .Evaluate(CollectionDocumentBuilder.FromResults(coletores), matrizCompleta);

            return new CheckupRun
            {
                ToolVersion = "0.1.0",
                StartedAt = new DateTimeOffset(2026, 8, 11, 9, 30, 0, TimeSpan.FromHours(-3)),
                FinishedAt = new DateTimeOffset(2026, 8, 11, 9, 31, 12, TimeSpan.FromHours(-3)),
                Elevated = true,
                Technician = "Gabriel Oss",
                DiagnosticId = "DIAG-2026-014",
                HostLocale = "pt-BR",
                ClientName = "Cliente Exemplo",
                ClientUnit = "Matriz",
                MachineLabel = "ADM-04",
                Responsible = "Maria",
                Department = "Administrativo",
                Collectors = coletores,
                Findings = avaliacao.Result.Findings,
                Score = avaliacao.Result.Score
            };
        }

        internal static IList<CollectorResult> Coletores(JObject documento)
        {
            var blocos = documento["collectors"] as JArray ?? new JArray();

            return blocos.OfType<JObject>().Select(bloco => new CollectorResult
            {
                Id = (string)bloco["id"],
                DisplayName = (string)bloco["displayName"],
                Status = Status((string)bloco["status"]),
                SkipReason = (string)bloco["skipReason"],
                RequiresElevation = (bool?)bloco["requiresElevation"] ?? false,
                DurationMs = (long?)bloco["durationMs"] ?? 0,
                TimedOut = (bool?)bloco["timedOut"] ?? false,
                Summary = (string)bloco["summary"],
                Errors = new List<CollectorError>(),
                Data = bloco["data"] is JObject ? bloco["data"] : null
            }).ToList();
        }

        private static CollectorStatus Status(string status)
        {
            if (status == "Completed") return CollectorStatus.Completed;
            if (status == "Skipped") return CollectorStatus.Skipped;

            return CollectorStatus.Failed;
        }

        internal static string PastaTemporaria()
        {
            var pasta = Path.Combine(Path.GetTempPath(), "epicora-checkup-testes",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(pasta);

            return pasta;
        }

        /// <summary>
        /// Sobe a partir da pasta de saída até achar a raiz — os testes leem a matriz e as
        /// fixtures REAIS do repositório, não cópias embutidas como recurso.
        ///
        /// O caminho deste arquivo entra como segunda tentativa para o caso de a saída do build
        /// ficar fora da árvore do repositório, que é o que acontece quando alguém compila
        /// estes mesmos fontes num andaime de fora — por exemplo para rodar os testes num Mac,
        /// já que net472 não executa aqui.
        /// </summary>
        private static string AcharRaiz(
            [System.Runtime.CompilerServices.CallerFilePath] string arquivoDesteFonte = null)
        {
            var raiz = SubirAte(AppDomain.CurrentDomain.BaseDirectory)
                       ?? SubirAte(Path.GetDirectoryName(arquivoDesteFonte));

            if (raiz != null) return raiz;

            throw new DirectoryNotFoundException(
                "não achei a raiz do repositório subindo de " + AppDomain.CurrentDomain.BaseDirectory);
        }

        private static string SubirAte(string inicio)
        {
            if (string.IsNullOrEmpty(inicio)) return null;

            var pasta = new DirectoryInfo(inicio);

            while (pasta != null)
            {
                // Marcadores da raiz: as duas pastas que definem o contrato do projeto.
                if (Directory.Exists(Path.Combine(pasta.FullName, "rules")) &&
                    Directory.Exists(Path.Combine(pasta.FullName, "schema")))
                {
                    return pasta.FullName;
                }

                pasta = pasta.Parent;
            }

            return null;
        }
    }
}
