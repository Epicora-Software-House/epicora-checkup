using System;
using System.Threading;
using EpicoraCheckup.Core.Contracts;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Collectors.Collectors
{
    /// <summary>
    /// Eventos críticos — janela de 30 dias.
    ///
    /// **Não lê evento nenhum ainda, e isso é decisão registrada, não pendência esquecida.**
    /// O <c>rules/event-ids.json</c> já tem os IDs levantados na documentação oficial
    /// (2026-08-03), mas <c>validUntil</c> segue nulo porque a validação de campo não foi
    /// feita. Enquanto for nulo NÃO avaliamos — EST-001..003 resolvem Indeterminate, que é a
    /// degradação segura do ADR-005 aplicada aqui.
    ///
    /// Ligar a avaliação exige, nesta ordem: rodar a sonda com filtro de provedor, conferir a
    /// contagem contra máquina de histórico conhecido, e corrigir a agregação de EST-003 para
    /// recorrência por aplicação.
    ///
    /// **NUNCA coletar o canal Security nem eventos de logon de usuário** (doc 01 §7.1). É
    /// limite de privacidade, não de escopo: quem lê o relatório precisa poder afirmar que a
    /// ferramenta não sabe a que horas alguém entrou na máquina.
    /// </summary>
    public sealed class EventsCollector : CollectorBase
    {
        private const int WindowDays = 30;

        public override string Id
        {
            get { return "events"; }
        }

        public override string DisplayName
        {
            get { return "Eventos críticos"; }
        }

        public override int EstimatedSeconds
        {
            get { return 1; }
        }

        protected override JObject Read(
            CollectionContext context, ErrorSink errors, CancellationToken cancellationToken)
        {
            var data = new JObject();

            data["windowDays"] = WindowDays;
            data["windowStartedAt"] = Payload.Moment(DateTimeOffset.Now.AddDays(-WindowDays));
            data["evaluated"] = false;
            data["reason"] =
                "rules/event-ids.json com IDs levantados na documentação oficial, mas validUntil nulo — " +
                "validação de campo pendente. Rode Test-DataSources.ps1 numa máquina de histórico " +
                "conhecido e confira as contagens.";
            data["unexpectedShutdowns"] = null;
            data["diskErrors"] = null;
            data["criticalApplicationErrors"] = null;
            data["matches"] = null;

            return Payload.Sanitized(data);
        }

        protected override string Summarize(JObject data)
        {
            return "IDs de evento não validados em campo — não avaliado";
        }
    }
}
