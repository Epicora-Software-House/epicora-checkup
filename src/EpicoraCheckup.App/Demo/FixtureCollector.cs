using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using EpicoraCheckup.Core.Contracts;
using EpicoraCheckup.Core.Model;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.App.Demo
{
    /// <summary>
    /// Reproduz um coletor a partir de um bloco já gravado num JSON de coleta.
    ///
    /// **Por que existe.** Os coletores reais só podem ser portados do `.ps1` depois do
    /// pré-voo em Windows — é dado de campo que decide o que cada fonte devolve. Mas as
    /// telas, os textos de cliente e a avaliação da matriz precisam ser revisados ANTES
    /// disso, senão a revisão do comercial fica bloqueada por trabalho de campo.
    ///
    /// Então o modo demonstração roda o fluxo inteiro — as cinco telas, o motor de regras de
    /// verdade sobre a matriz de verdade — com os dados vindo de uma fixture.
    ///
    /// **Por que não grava arquivo.** Um relatório derivado de fixture não pode circular. Se
    /// gravasse, bastaria um arquivo esquecido numa pasta para alguém entregar dado
    /// inventado a um cliente, e nenhum aviso na tela evita isso depois que o arquivo existe.
    /// A proteção é não produzir o artefato. A faixa roxa em todas as telas é o segundo
    /// aviso, não o primeiro.
    /// </summary>
    internal sealed class FixtureCollector : ICollector
    {
        private readonly JObject _block;

        private FixtureCollector(JObject block)
        {
            _block = block;
        }

        public string Id => (string)_block["id"];

        public string DisplayName => (string)_block["displayName"] ?? Id;

        // A fixture já registra o que foi ignorado por falta de privilégio. Reaplicar o gate
        // de elevação aqui sobrescreveria o cenário gravado — e a fixture amarela existe
        // justamente para exercitar a execução sem elevação.
        public bool RequiresElevation => false;

        public int EstimatedSeconds => 1;

        public CollectorResult Collect(CollectionContext context, CancellationToken cancellationToken)
        {
            // Espera proporcional à duração gravada, com teto, para a tela 2 se comportar
            // como numa coleta real — estado mudando etapa por etapa em vez de tudo de uma
            // vez. Sem isto não há o que revisar na tela 2.
            var gravada = (long?)_block["durationMs"] ?? 0;
            var espera = (int)Math.Min(Math.Max(gravada / 12, 40), 400);

            if (cancellationToken.WaitHandle.WaitOne(espera))
                cancellationToken.ThrowIfCancellationRequested();

            var status = ParseStatus((string)_block["status"]);

            return new CollectorResult
            {
                Id = Id,
                DisplayName = DisplayName,
                Status = status,
                SkipReason = (string)_block["skipReason"],
                RequiresElevation = (bool?)_block["requiresElevation"] ?? false,
                TimedOut = (bool?)_block["timedOut"] ?? false,
                Summary = (string)_block["summary"],
                Errors = ParseErrors(_block["errors"] as JArray),
                Data = _block["data"]
            };
        }

        private static CollectorStatus ParseStatus(string status)
        {
            switch (status)
            {
                case "Completed": return CollectorStatus.Completed;
                case "Skipped": return CollectorStatus.Skipped;
                default: return CollectorStatus.Failed;
            }
        }

        private static IList<CollectorError> ParseErrors(JArray errors)
        {
            var list = new List<CollectorError>();
            if (errors == null) return list;

            foreach (var error in errors.OfType<JObject>())
            {
                list.Add(new CollectorError
                {
                    Source = (string)error["source"],
                    Message = (string)error["message"],
                    Detail = (string)error["detail"]
                });
            }

            return list;
        }

        // ------------------------------------------------------------ carga

        /// <summary>
        /// Lê a fixture e devolve um coletor por bloco, na ordem gravada.
        ///
        /// Devolve SÓ os coletores, e não o documento cru, de propósito: a avaliação tem de
        /// rodar sobre os resultados que os coletores produzem, como em produção. Expor o
        /// documento aqui seria convidar o atalho que faz a demonstração testar outro caminho.
        /// </summary>
        internal static IReadOnlyList<ICollector> Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"arquivo de demonstração não encontrado: {path}", path);

            var text = File.ReadAllText(path);
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);

            var document = JObject.Parse(text);

            var blocks = document["collectors"] as JArray;
            if (blocks == null || blocks.Count == 0)
                throw new InvalidDataException($"{Path.GetFileName(path)} não tem a lista \"collectors\" — não serve como fixture de demonstração");

            return blocks
                .OfType<JObject>()
                .Where(block => (string)block["id"] != null)
                .Select(block => new FixtureCollector(block))
                .Cast<ICollector>()
                .ToList();
        }
    }
}
