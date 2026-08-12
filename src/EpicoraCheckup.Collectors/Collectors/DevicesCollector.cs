using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EpicoraCheckup.Collectors.Sources;
using EpicoraCheckup.Core.Contracts;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Collectors.Collectors
{
    /// <summary>
    /// Placa de vídeo e dispositivos com problema. Alimenta o inventário do relatório; a
    /// contagem de dispositivos com problema é candidata a regra (HW-006, hoje desabilitada
    /// por falta de <c>clientText</c> aprovado pelo comercial).
    /// </summary>
    public sealed class DevicesCollector : CollectorBase
    {
        /// <summary>
        /// Descrições dos códigos que aparecem em máquina de cliente. O mapa é parcial: código
        /// fora dele vira <c>null</c>, e o relatório mostra só o número — melhor que inventar
        /// uma explicação errada para o cliente ler.
        /// </summary>
        private static readonly Dictionary<int, string> ProblemDescriptions = new Dictionary<int, string>
        {
            { 10, "O dispositivo não pode ser iniciado" },
            { 22, "O dispositivo está desabilitado" },
            { 28, "Os drivers deste dispositivo não estão instalados" },
            { 31, "O dispositivo não está funcionando corretamente" },
            { 43, "O Windows parou este dispositivo por relatar problemas" }
        };

        public override string Id
        {
            get { return "devices"; }
        }

        public override string DisplayName
        {
            get { return "Placa de vídeo e dispositivos"; }
        }

        public override int EstimatedSeconds
        {
            get { return 4; }
        }

        protected override JObject Read(
            CollectionContext context, ErrorSink errors, CancellationToken cancellationToken)
        {
            var video = Wmi.Instances(Wmi.CimV2, "Win32_VideoController");

            // O filtro vai na CONSULTA, não em memória: Win32_PnPEntity lista centenas de
            // instâncias e trazer todas custa segundos do orçamento de 90 s.
            var problems = Wmi.Instances(Wmi.CimV2, "Win32_PnPEntity", "ConfigManagerErrorCode <> 0");

            var controllers = video.Select(controller =>
            {
                var entry = new JObject();

                entry["name"] = controller.Text("Name");
                entry["driverVersion"] = controller.Text("DriverVersion");
                entry["driverDate"] = Payload.Date(controller.Moment("DriverDate"));
                entry["adapterRamBytes"] = controller.Long("AdapterRAM");
                entry["currentResolution"] = Resolution(controller);

                return entry;
            }).ToList();

            var problemEntries = problems.Select(device =>
            {
                var code = device.Int("ConfigManagerErrorCode");

                var entry = new JObject();

                entry["name"] = device.Text("Name");
                entry["deviceClass"] = device.Text("PNPClass");
                entry["configManagerErrorCode"] = code;
                entry["errorDescription"] = Payload.Lookup(ProblemDescriptions, code);

                return entry;
            }).ToList();

            var data = new JObject();

            data["videoControllers"] = Payload.ArrayOrNull(controllers);
            data["problemDevices"] = Payload.Array(problemEntries);
            data["problemDeviceCount"] = problemEntries.Count;

            return Payload.Sanitized(data);
        }

        protected override string Summarize(JObject data)
        {
            var controllers = data["videoControllers"] as JArray;

            var video = controllers != null && controllers.Count > 0
                ? TextOf(controllers[0]["name"])
                : "vídeo não identificado";

            return string.Format("{0}, {1} dispositivo(s) com problema",
                video, data["problemDeviceCount"]);
        }

        private static string Resolution(PropertyBag controller)
        {
            var horizontal = controller.Int("CurrentHorizontalResolution");
            var vertical = controller.Int("CurrentVerticalResolution");

            return horizontal.HasValue && vertical.HasValue
                ? horizontal.Value + "x" + vertical.Value
                : null;
        }
    }
}
