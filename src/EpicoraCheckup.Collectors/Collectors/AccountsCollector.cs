using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using EpicoraCheckup.Collectors.Sources;
using EpicoraCheckup.Core.Contracts;
using Newtonsoft.Json.Linq;

namespace EpicoraCheckup.Collectors.Collectors
{
    /// <summary>
    /// Contas e privilégios. Sustenta SEC-007 — usuário do dia a dia como administrador local
    /// é a medida de hardening de maior efeito e menor custo numa estação de trabalho.
    /// </summary>
    public sealed class AccountsCollector : CollectorBase
    {
        /// <summary>
        /// SID do grupo Administradores. **O nome do grupo é LOCALIZADO** — "Administradores"
        /// em pt-BR, "Administrators" em inglês —, então resolver por nome quebra conforme o
        /// idioma da máquina. O SID é o mesmo em toda instalação de Windows.
        /// </summary>
        private const string AdministratorsSid = "S-1-5-32-544";

        public override string Id
        {
            get { return "accounts"; }
        }

        public override string DisplayName
        {
            get { return "Contas e privilégios"; }
        }

        public override int EstimatedSeconds
        {
            get { return 4; }
        }

        protected override JObject Read(
            CollectionContext context, ErrorSink errors, CancellationToken cancellationToken)
        {
            var administrators = new List<PropertyBag>();
            var resolvedBySid = false;

            var group = errors.Read("Win32_Group S-1-5-32-544", () =>
                Wmi.Instances(Wmi.CimV2, "Win32_Group", "SID='" + AdministratorsSid + "'").FirstOrDefault());

            if (group != null)
            {
                var domain = Escape(group.Text("Domain"));
                var name = Escape(group.Text("Name"));

                var members = errors.Read("ASSOCIATORS OF Win32_Group", () => Wmi.Query(Wmi.CimV2,
                    "ASSOCIATORS OF {Win32_Group.Domain='" + domain + "',Name='" + name + "'} " +
                    "WHERE ResultClass=Win32_Account"));

                if (members != null)
                {
                    resolvedBySid = true;
                    administrators.AddRange(members);
                }
            }

            string userName;
            string userSid;
            IList<string> groupSids;

            using (var identity = WindowsIdentity.GetCurrent())
            {
                userName = identity.Name;
                userSid = identity.User == null ? null : identity.User.Value;

                groupSids = identity.Groups == null
                    ? new List<string>()
                    : identity.Groups.Select(reference => reference.Value).ToList();
            }

            var accounts = errors.Read("Win32_UserAccount",
                () => Wmi.Instances(Wmi.CimV2, "Win32_UserAccount", "LocalAccount=TRUE"))
                ?? new List<PropertyBag>();

            var computer = Wmi.Instances(Wmi.CimV2, "Win32_ComputerSystem").FirstOrDefault();

            return AccountsFacts.Build(
                resolvedBySid,
                administrators,
                userName,
                userSid,
                groupSids,
                AdministratorsSid,
                accounts,
                computer);
        }

        protected override string Summarize(JObject data)
        {
            var admin = FlagOf(data["currentUser"]["isLocalAdmin"]);

            if (admin == true) return "Usuário do dia a dia é administrador local";
            if (admin == false) return "Usuário do dia a dia é usuário padrão";

            return "Privilégio do usuário não verificado";
        }

        /// <summary>Aspas simples num nome de domínio quebrariam a consulta WQL.</summary>
        private static string Escape(string value)
        {
            return value == null ? string.Empty : value.Replace("'", "\\'");
        }
    }

    /// <summary>Derivação pura do payload de <c>accounts</c>.</summary>
    public static class AccountsFacts
    {
        private static readonly Dictionary<int, string> PrincipalTypes = new Dictionary<int, string>
        {
            { 1, "User" }, { 2, "Group" }
        };

        public static JObject Build(
            bool resolvedBySid,
            IList<PropertyBag> administrators,
            string currentUserName,
            string currentUserSid,
            IList<string> tokenGroupSids,
            string administratorsSid,
            IList<PropertyBag> localAccounts,
            PropertyBag computer)
        {
            var members = administrators.Select(member =>
            {
                var entry = new JObject();

                entry["name"] = (member.Text("Domain") ?? string.Empty) + "\\" + (member.Text("Name") ?? string.Empty);
                entry["sid"] = member.Text("SID");
                entry["domain"] = member.Text("Domain");
                entry["principalType"] = Payload.Lookup(PrincipalTypes, member.Int("SIDType")) ?? "Unknown";
                entry["disabled"] = member.Flag("Disabled");

                return entry;
            }).ToList();

            var directMember = members.Any(member => (string)member["sid"] == currentUserSid);
            var tokenHasAdmin = tokenGroupSids.Contains(administratorsSid);
            var groupInMembers = members.Any(member => (string)member["principalType"] == "Group");

            var accounts = localAccounts.Select(account =>
            {
                var entry = new JObject();

                entry["name"] = account.Text("Name");
                entry["sid"] = account.Text("SID");
                entry["disabled"] = account.Flag("Disabled");
                entry["passwordRequired"] = account.Flag("PasswordRequired");
                entry["passwordExpires"] = account.Flag("PasswordExpires");

                return entry;
            }).ToList();

            // A conta Convidado é sempre o RID 501, em qualquer idioma.
            var guest = localAccounts.FirstOrDefault(
                account => (account.Text("SID") ?? string.Empty).EndsWith("-501", StringComparison.Ordinal));

            var domainJoined = computer != null && computer.Flag("PartOfDomain") == true;

            var data = new JObject();

            data["administratorsGroupResolvedBySid"] = resolvedBySid;
            data["localAdministrators"] = Payload.ArrayOrNull(members);

            data["currentUser"] = new JObject
            {
                ["name"] = currentUserName,
                ["sid"] = currentUserSid,
                ["isLocalAdmin"] = IsLocalAdmin(resolvedBySid, directMember, tokenHasAdmin, groupInMembers),
                ["isDomainAccount"] = IsDomainAccount(currentUserName, computer, domainJoined)
            };

            data["localAccounts"] = Payload.ArrayOrNull(accounts);
            data["guestAccountEnabled"] = guest == null ? null : (bool?)!(guest.Flag("Disabled") ?? false);

            return Payload.Sanitized(data);
        }

        /// <summary>
        /// Se o usuário da sessão é administrador local.
        ///
        /// SEC-007 é Alto, e um falso NEGATIVO aqui faz a regra dizer "conforme" numa máquina
        /// que não é — a regra 1 de contribuição violada pelo outro lado, e pior que
        /// Indeterminate.
        ///
        /// **NÃO confiar só no token: MEDIDO EM CAMPO (sonda, 2026-07-29, duas rodadas) que o
        /// token filtrado do UAC NÃO carrega S-1-5-32-544 numa sessão sem elevação**, mesmo
        /// para quem é administrador local. Só o token daria <c>false</c> para um admin.
        ///
        /// Ordem de decisão:
        ///   <c>true</c>  — está direto na lista de membros (legível SEM elevação) OU o token
        ///                  traz o SID do grupo (pega membro indireto, mas só quando elevado)
        ///   <c>null</c>  — nenhum dos dois E existe GRUPO entre os membros: a associação pode
        ///                  ser indireta por esse grupo e não há como saber sem consultar o
        ///                  diretório. Nunca <c>false</c> por ignorância
        ///   <c>false</c> — nenhum dos dois e não há grupo entre os membros
        /// </summary>
        public static bool? IsLocalAdmin(
            bool resolvedBySid, bool directMember, bool tokenHasAdminSid, bool groupInMembers)
        {
            if (!resolvedBySid) return null;
            if (directMember || tokenHasAdminSid) return true;
            if (groupInMembers) return null;

            return false;
        }

        private static bool? IsDomainAccount(string userName, PropertyBag computer, bool domainJoined)
        {
            if (!domainJoined) return false;
            if (userName == null || computer == null) return null;

            var domain = computer.Text("Domain");
            if (domain == null) return null;

            var netbios = domain.Split('.')[0];

            return userName.StartsWith(netbios + "\\", StringComparison.OrdinalIgnoreCase);
        }
    }
}
