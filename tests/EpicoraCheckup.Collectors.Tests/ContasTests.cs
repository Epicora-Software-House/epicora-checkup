using System.Collections.Generic;
using EpicoraCheckup.Collectors.Collectors;
using Xunit;
using static EpicoraCheckup.Collectors.Tests.Fonte;

namespace EpicoraCheckup.Collectors.Tests
{
    /// <summary>
    /// Contas e privilégios — SEC-007.
    ///
    /// Aqui o erro caro é o falso NEGATIVO: dizer "usuário padrão" para quem é administrador
    /// local faz a regra afirmar conformidade numa máquina que não é conforme. É a regra 1 de
    /// contribuição violada pelo outro lado, e pior que Indeterminate.
    /// </summary>
    public sealed class ContasTests
    {
        private const string SidAdministradores = "S-1-5-32-544";
        private const string SidDoUsuario = "S-1-5-21-1111111111-2222222222-3333333333-1001";

        [Fact]
        public void Membro_direto_sem_elevacao_e_administrador_mesmo_sem_o_SID_no_token()
        {
            // MEDIDO EM CAMPO (DELL-G15, 2026-07-29): o token filtrado do UAC NÃO carrega
            // S-1-5-32-544 numa sessão sem elevação, mesmo para quem é administrador local.
            // Confiar só no token era o falso negativo de SEC-007.
            Assert.True(AccountsFacts.IsLocalAdmin(
                resolvedBySid: true, directMember: true, tokenHasAdminSid: false, groupInMembers: false));
        }

        [Fact]
        public void Token_com_o_SID_do_grupo_basta_quando_a_associacao_e_indireta()
        {
            Assert.True(AccountsFacts.IsLocalAdmin(
                resolvedBySid: true, directMember: false, tokenHasAdminSid: true, groupInMembers: true));
        }

        [Fact]
        public void Grupo_entre_os_membros_sem_confirmacao_resolve_null_e_nunca_false()
        {
            // A associação pode ser indireta por esse grupo e não há como saber sem consultar o
            // diretório. Responder false aqui seria afirmar conformidade por ignorância.
            Assert.Null(AccountsFacts.IsLocalAdmin(
                resolvedBySid: true, directMember: false, tokenHasAdminSid: false, groupInMembers: true));
        }

        [Fact]
        public void Sem_grupo_e_sem_confirmacao_a_resposta_e_false_de_verdade()
        {
            Assert.False(AccountsFacts.IsLocalAdmin(
                resolvedBySid: true, directMember: false, tokenHasAdminSid: false, groupInMembers: false));
        }

        [Fact]
        public void Grupo_nao_resolvido_pelo_SID_invalida_toda_a_conclusao()
        {
            // Sem resolver pelo SID, o grupo teria sido achado por nome — e nome de grupo é
            // localizado. SEC-007 resolve Indeterminate.
            Assert.Null(AccountsFacts.IsLocalAdmin(
                resolvedBySid: false, directMember: true, tokenHasAdminSid: true, groupInMembers: false));
        }

        [Fact]
        public void Conta_convidado_e_reconhecida_pelo_RID_e_nao_pelo_nome()
        {
            // "Convidado" em pt-BR, "Guest" em inglês. O RID 501 é o mesmo em toda instalação.
            var dados = AccountsFacts.Build(
                resolvedBySid: true,
                administrators: Nenhum(),
                currentUserName: @"MAQUINA\gabriel",
                currentUserSid: SidDoUsuario,
                tokenGroupSids: new List<string>(),
                administratorsSid: SidAdministradores,
                localAccounts: Lista(
                    Bag("Name", "Convidado", "SID", "S-1-5-21-1111111111-2222222222-3333333333-501",
                        "Disabled", false),
                    Bag("Name", "gabriel", "SID", SidDoUsuario, "Disabled", false)),
                computer: Bag("PartOfDomain", false));

            Assert.True((bool)dados["guestAccountEnabled"]);
            Assert.False((bool)dados["currentUser"]["isDomainAccount"]);
        }

        [Fact]
        public void Usuario_de_dominio_e_identificado_pelo_prefixo_NetBIOS()
        {
            var dados = AccountsFacts.Build(
                resolvedBySid: true,
                administrators: Lista(Bag(
                    "Domain", "EPICORA", "Name", "gabriel", "SID", SidDoUsuario, "SIDType", (ushort)1)),
                currentUserName: @"EPICORA\gabriel",
                currentUserSid: SidDoUsuario,
                tokenGroupSids: new List<string>(),
                administratorsSid: SidAdministradores,
                localAccounts: Nenhum(),
                computer: Bag("PartOfDomain", true, "Domain", "epicora.local"));

            Assert.True((bool)dados["currentUser"]["isDomainAccount"]);
            Assert.True((bool)dados["currentUser"]["isLocalAdmin"]);
            Assert.Equal(@"EPICORA\gabriel", (string)dados["localAdministrators"][0]["name"]);
            Assert.Equal("User", (string)dados["localAdministrators"][0]["principalType"]);
        }
    }
}
