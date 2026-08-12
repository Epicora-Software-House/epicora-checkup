using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using EpicoraCheckup.Collectors.Sources;
using EpicoraCheckup.Core.Contracts;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Collectors.Collectors
{
    /// <summary>
    /// Segurança e criptografia.
    ///
    /// **Não é marcado <c>RequiresElevation</c>, e isso foi MEDIDO EM CAMPO:** só o
    /// <c>Win32_EncryptableVolume</c> exige privilégio. Firewall, RDP, SMBv1 e UAC respondem
    /// sem elevação — e eram perdidos de graça (SEC-006, SEC-008, SEC-009). Cada sub-leitura
    /// tem tratamento próprio; o BitLocker degrada para null e SEC-004/005 resolvem
    /// Indeterminate, que é o correto.
    /// </summary>
    public sealed class SecurityCollector : CollectorBase
    {
        private const string TerminalServerKey = @"SYSTEM\CurrentControlSet\Control\Terminal Server";
        private const string RdpTcpKey = @"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp";
        private const string PoliciesSystemKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";

        public override string Id
        {
            get { return "security"; }
        }

        public override string DisplayName
        {
            get { return "Segurança e criptografia"; }
        }

        public override int EstimatedSeconds
        {
            get { return 5; }
        }

        protected override JObject Read(
            CollectionContext context, ErrorSink errors, CancellationToken cancellationToken)
        {
            var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";

            var data = new JObject();

            data["bitlocker"] = ReadBitLocker(errors, systemDrive);
            data["firewall"] = ReadFirewall(errors);
            data["rdp"] = ReadRdp();
            data["smb1"] = ReadSmb1(errors);
            data["uac"] = ReadUac();

            return Payload.Sanitized(data);
        }

        protected override string Summarize(JObject data)
        {
            var protegido = FlagOf(data["bitlocker"]["systemVolumeProtected"]);
            var firewall = FlagOf(data["firewall"]["anyProfileDisabled"]);

            var bitlocker = protegido == true ? "BitLocker ativo"
                : protegido == false ? "sem BitLocker"
                : "BitLocker não verificado";

            var estadoFirewall = firewall == true ? "firewall desativado em algum perfil"
                : firewall == false ? "firewall ativo"
                : "firewall não verificado";

            return bitlocker + ", " + estadoFirewall;
        }

        // ------------------------------------------------------------ BitLocker

        private static readonly Dictionary<int, string> ProtectionStates = new Dictionary<int, string>
        {
            { 0, "Off" }, { 1, "On" }
        };

        private static readonly Dictionary<int, string> ConversionStates = new Dictionary<int, string>
        {
            { 0, "FullyDecrypted" }, { 1, "FullyEncrypted" },
            { 2, "EncryptionInProgress" }, { 3, "DecryptionInProgress" }
        };

        private static readonly Dictionary<int, string> EncryptionMethods = new Dictionary<int, string>
        {
            { 3, "Aes128" }, { 4, "Aes256" }, { 6, "XtsAes128" }, { 7, "XtsAes256" }
        };

        /// <summary>
        /// BitLocker.
        ///
        /// A sonda mediu que o namespace EXISTE em Windows 11 Home — a premissa de que só Pro
        /// o tem estava errada. Por isso distinguir os dois erros passa a importar:
        ///
        ///   acesso negado (sessão sem elevação) → <c>null</c>, não sabemos
        ///   qualquer outro (namespace ausente) → <c>false</c>, capacidade ausente de fato
        ///
        /// Marcar <c>false</c> por falta de privilégio faria a ausência de criptografia parecer
        /// medida quando só houve falta de permissão — e SEC-004 é sobre notebook que sai da
        /// empresa com dado de cliente dentro.
        /// </summary>
        private static JObject ReadBitLocker(ErrorSink errors, string systemDrive)
        {
            var bitlocker = new JObject
            {
                ["available"] = null,
                ["systemVolumeProtected"] = null,
                ["volumes"] = null
            };

            IList<PropertyBag> volumes;

            try
            {
                volumes = Wmi.Instances(Wmi.VolumeEncryption, "Win32_EncryptableVolume");
            }
            catch (Exception exception)
            {
                errors.Record("Win32_EncryptableVolume", exception);
                bitlocker["available"] = IsAccessDenied(exception) ? (bool?)null : false;
                return bitlocker;
            }

            var entries = volumes.Select(volume =>
            {
                var entry = new JObject();

                entry["driveLetter"] = volume.Text("DriveLetter");
                entry["protectionStatus"] =
                    Payload.Lookup(ProtectionStates, volume.Int("ProtectionStatus")) ?? "Unknown";
                entry["conversionStatus"] =
                    Payload.Lookup(ConversionStates, volume.Int("ConversionStatus"));
                entry["encryptionMethod"] =
                    Payload.Lookup(EncryptionMethods, volume.Int("EncryptionMethod"));

                return entry;
            }).ToList();

            var system = entries.FirstOrDefault(entry => string.Equals(
                (string)entry["driveLetter"], systemDrive, StringComparison.OrdinalIgnoreCase));

            bitlocker["available"] = true;
            bitlocker["volumes"] = Payload.ArrayOrNull(entries);
            bitlocker["systemVolumeProtected"] = system == null
                ? (bool?)null
                : (string)system["protectionStatus"] == "On";

            return bitlocker;
        }

        /// <summary>
        /// Acesso negado é resposta diferente de "não existe". As duas chegam como exceção, e
        /// só o código de erro as separa.
        /// </summary>
        private static bool IsAccessDenied(Exception exception)
        {
            if (exception is UnauthorizedAccessException) return true;

            var management = exception as ManagementException;

            return management != null &&
                   (management.ErrorCode == ManagementStatus.AccessDenied ||
                    management.ErrorCode == ManagementStatus.PrivilegeNotHeld);
        }

        // ------------------------------------------------------------ firewall

        /// <summary>
        /// Firewall por perfil.
        ///
        /// <c>anyProfileDisabled</c> só é preenchido quando TODOS os perfis responderam: com um
        /// perfil ilegível, "nenhum desativado" seria afirmação sobre o que não foi lido.
        /// </summary>
        private static JObject ReadFirewall(ErrorSink errors)
        {
            var firewall = new JObject { ["anyProfileDisabled"] = null, ["profiles"] = null };

            var profiles = errors.Read("MSFT_NetFirewallProfile",
                () => Wmi.Instances(Wmi.StandardCimV2, "MSFT_NetFirewallProfile"));

            if (profiles == null || profiles.Count == 0) return firewall;

            var entries = profiles.Select(profile => new JObject
            {
                ["name"] = profile.Text("Name"),
                ["enabled"] = profile.Flag("Enabled")
            }).ToList();

            firewall["profiles"] = Payload.ArrayOrNull(entries);

            var conhecidos = entries.Count(entry => entry["enabled"].Type != JTokenType.Null);
            if (conhecidos == entries.Count)
                firewall["anyProfileDisabled"] = entries.Any(entry => (bool?)entry["enabled"] == false);

            return firewall;
        }

        // ------------------------------------------------------------ RDP, SMBv1 e UAC

        private static JObject ReadRdp()
        {
            var rdp = new JObject { ["enabled"] = null, ["nlaRequired"] = null, ["port"] = null };

            var deny = RegistryReader.Int(RegistryHive.LocalMachine, TerminalServerKey, "fDenyTSConnections");
            if (!deny.HasValue) return rdp;

            var enabled = deny.Value == 0;
            rdp["enabled"] = enabled;

            // NLA e porta só fazem sentido com RDP ligado, e ler à toa é ruído no relatório.
            if (!enabled) return rdp;

            var nla = RegistryReader.Int(RegistryHive.LocalMachine, RdpTcpKey, "UserAuthentication");
            if (nla.HasValue) rdp["nlaRequired"] = nla.Value == 1;

            rdp["port"] = RegistryReader.Int(RegistryHive.LocalMachine, RdpTcpKey, "PortNumber");

            return rdp;
        }

        private static readonly Dictionary<int, string> FeatureStates = new Dictionary<int, string>
        {
            { 1, "Enabled" }, { 2, "Disabled" }, { 3, "Absent" }
        };

        private static JObject ReadSmb1(ErrorSink errors)
        {
            var smb1 = new JObject { ["enabled"] = null, ["featureState"] = null };

            var feature = errors.Read("Win32_OptionalFeature SMB1Protocol",
                () => Wmi.Instances(Wmi.CimV2, "Win32_OptionalFeature", "Name='SMB1Protocol'").FirstOrDefault());

            if (feature == null) return smb1;

            var state = feature.Int("InstallState");

            smb1["featureState"] = Payload.Lookup(FeatureStates, state);

            if (state == 1) smb1["enabled"] = true;
            else if (state == 2 || state == 3) smb1["enabled"] = false;

            return smb1;
        }

        private static JObject ReadUac()
        {
            var uac = new JObject { ["enabled"] = null, ["consentPromptLevel"] = null };

            if (!RegistryReader.KeyExists(RegistryHive.LocalMachine, PoliciesSystemKey)) return uac;

            var enableLua = RegistryReader.Int(RegistryHive.LocalMachine, PoliciesSystemKey, "EnableLUA");

            uac["enabled"] = (enableLua ?? 0) == 1;
            uac["consentPromptLevel"] =
                RegistryReader.Int(RegistryHive.LocalMachine, PoliciesSystemKey, "ConsentPromptBehaviorAdmin");

            return uac;
        }
    }
}
