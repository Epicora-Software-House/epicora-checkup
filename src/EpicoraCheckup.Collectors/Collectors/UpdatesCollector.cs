using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using EpicoraCheckup.Collectors.Sources;
using EpicoraCheckup.Core.Contracts;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Collectors.Collectors
{
    /// <summary>
    /// Atualizações do Windows.
    ///
    /// <c>coverageIsPartial</c> é SEMPRE <c>true</c> e isso é estrutural, não um estado:
    /// <c>Win32_QuickFixEngineering</c> não lista atualizações cumulativas modernas nem as
    /// entregues por outros mecanismos (doc 02 §4.4). **Nenhuma regra pode concluir
    /// "desatualizado" só a partir desta lista.**
    /// </summary>
    public sealed class UpdatesCollector : CollectorBase
    {
        private const string WindowsUpdatePolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";

        public override string Id
        {
            get { return "updates"; }
        }

        public override string DisplayName
        {
            get { return "Atualizações do Windows"; }
        }

        public override int EstimatedSeconds
        {
            get { return 6; }
        }

        protected override JObject Read(
            CollectionContext context, ErrorSink errors, CancellationToken cancellationToken)
        {
            var hotfixes = Wmi.Instances(Wmi.CimV2, "Win32_QuickFixEngineering");

            var service = errors.Read("Win32_Service wuauserv",
                () => Wmi.Instances(Wmi.CimV2, "Win32_Service", "Name='wuauserv'").FirstOrDefault());

            bool? serviceEnabled = null;
            if (service != null)
            {
                var startMode = service.Text("StartMode");
                serviceEnabled = startMode == null
                    ? (bool?)null
                    : !string.Equals(startMode, "Disabled", StringComparison.OrdinalIgnoreCase);
            }

            // Existir a política já é a resposta — o conteúdo do valor não importa.
            var wsus = RegistryReader.HasValue(RegistryHive.LocalMachine, WindowsUpdatePolicyKey, "WUServer");

            return UpdatesFacts.Build(hotfixes, serviceEnabled, wsus, DateTimeOffset.Now);
        }

        protected override string Summarize(JObject data)
        {
            var days = data["daysSinceLastUpdate"];
            if (days == null || days.Type == JTokenType.Null) return "Nenhuma atualização registrada";

            return "Última atualização registrada há " + days + " dias";
        }
    }

    /// <summary>Derivação pura do payload de <c>updates</c>.</summary>
    public static class UpdatesFacts
    {
        public static JObject Build(
            IList<PropertyBag> hotfixes, bool? serviceEnabled, bool wsusConfigured, DateTimeOffset now)
        {
            var entries = new List<JObject>();
            var dates = new List<DateTime>();

            foreach (var hotfix in hotfixes ?? new List<PropertyBag>())
            {
                var installed = HotfixDate.Parse(hotfix.Raw("InstalledOn"), now);
                if (installed.HasValue) dates.Add(installed.Value);

                var entry = new JObject();

                entry["hotfixId"] = hotfix.Text("HotFixID");
                entry["installedOn"] = installed.HasValue
                    ? new JValue(installed.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                    : (JToken)JValue.CreateNull();
                entry["description"] = hotfix.Text("Description");

                entries.Add(entry);
            }

            DateTime? last = dates.Count == 0 ? (DateTime?)null : dates.Max();

            var data = new JObject();

            data["hotfixes"] = Payload.ArrayOrNull(entries);
            data["coverageIsPartial"] = true;
            data["lastUpdateDate"] = last.HasValue
                ? new JValue(last.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                : (JToken)JValue.CreateNull();
            data["daysSinceLastUpdate"] = last.HasValue
                ? (int?)Math.Max(0, (int)(now.Date - last.Value.Date).TotalDays)
                : null;
            data["windowsUpdateServiceEnabled"] = serviceEnabled;
            data["wsusConfigured"] = wsusConfigured;

            return Payload.Sanitized(data);
        }
    }

    /// <summary>
    /// Data de instalação de um hotfix.
    ///
    /// <c>Win32_QuickFixEngineering.InstalledOn</c> é declarada como **texto** no MOF, e o
    /// conteúdo varia com a origem da atualização e com o idioma da máquina — o
    /// <c>Get-HotFix</c> tem conversão própria justamente por isso. Um teste de tipo
    /// (<c>-is [datetime]</c>) descarta a maioria dos valores reais e deixa
    /// <c>daysSinceLastUpdate</c> null em máquina que tem histórico — perdendo a base de
    /// SEC-010.
    ///
    /// Ordem de tentativa, da menos ambígua para a mais: CIM_DATETIME, <c>yyyyMMdd</c>,
    /// formato da máquina, formato invariante. Data implausível vira <c>null</c> — nunca um
    /// palpite, porque uma data errada aqui vira "esta máquina está há dois anos sem
    /// atualizar" num relatório entregue ao cliente.
    /// </summary>
    public static class HotfixDate
    {
        private static readonly Regex Compact = new Regex(@"^\d{8}$", RegexOptions.CultureInvariant);

        public static DateTime? Parse(object raw, DateTimeOffset now)
        {
            if (raw == null) return null;

            if (raw is DateTime) return Plausible((DateTime)raw, now);
            if (raw is DateTimeOffset) return Plausible(((DateTimeOffset)raw).DateTime, now);

            var text = (raw as string ?? Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty).Trim();
            if (text.Length == 0) return null;

            var cim = PropertyBag.ParseCimDateTime(text);
            if (cim.HasValue) return Plausible(cim.Value.DateTime, now);

            DateTime parsed;

            if (Compact.IsMatch(text) && DateTime.TryParseExact(
                    text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return Plausible(parsed, now);
            }

            // O texto vem formatado no idioma da máquina, que é o da cultura corrente.
            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed))
                return Plausible(parsed, now);

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                return Plausible(parsed, now);

            return null;
        }

        /// <summary>
        /// Nenhuma máquina foi atualizada antes do Windows 7 existir nem amanhã. Fora da faixa
        /// é lixo de firmware ou parse errado, e vale mais null que uma data inventada.
        /// </summary>
        private static DateTime? Plausible(DateTime value, DateTimeOffset now)
        {
            if (value.Year < 2009) return null;
            if (value.Date > now.Date.AddDays(1)) return null;

            return value.Date;
        }
    }
}
