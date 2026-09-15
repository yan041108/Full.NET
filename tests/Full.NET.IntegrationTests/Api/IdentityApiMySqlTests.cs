using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Identity;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class IdentityApiMySqlTests
{
    [TestMethod]
    public async Task Login_and_current_user_follow_secure_http_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await IdentityApiAssertions.VerifyLoginAsync(factory);
    }

    [TestMethod]
    public async Task Locale_preference_is_persisted_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await LocalePreferenceTests.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Last_super_administrator_is_protected_under_mysql_concurrency()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await SuperAdministratorConcurrencyAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Session_refresh_and_context_switch_races_are_linearized()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await SessionRaceAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_user_management_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await IdentityUserManagementAssertions.VerifyHostUserManagementContractAsync(factory);
    }

    [TestMethod]
    public async Task Host_user_roles_follow_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await IdentityUserRolesManagementAssertions.VerifyHostUserRolesContractAsync(factory);
    }

    [TestMethod]
    public async Task Host_role_management_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await IdentityRoleManagementAssertions.VerifyHostRoleManagementContractAsync(factory);
    }

    [TestMethod]
    public async Task Host_role_data_scope_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await IdentityRoleDataScopeManagementAssertions.VerifyHostRoleDataScopeContractAsync(factory);
    }

    [TestMethod]
    public async Task Host_role_field_grants_enforce_projection_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await IdentityRoleFieldGrantAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_menu_management_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await IdentityMenuManagementAssertions.VerifyHostMenuManagementContractAsync(factory);
    }

    [TestMethod]
    public async Task Host_online_sessions_follow_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await IdentityOnlineSessionAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Single_session_policy_revokes_previous_login_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            new Dictionary<string, string?>
            {
                ["Identity:SessionLoginPolicy"] = nameof(IdentitySessionLoginPolicy.SingleSession),
            });

        await IdentityOnlineSessionAssertions.VerifySingleSessionPolicyAsync(factory);
    }

    [TestMethod]
    public async Task Host_api_keys_follow_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await IdentityApiKeyAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_open_access_clients_follow_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await IdentityOpenAccessClientAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_signature_authentication_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await IdentitySignatureAuthenticationAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_module_catalog_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await IdentityModuleCatalogAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Oidc_stores_enforce_redemption_and_revocation_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            new Dictionary<string, string?>
            {
                ["Identity:Oidc:Enable"] = "true",
                ["Identity:Oidc:Issuer"] = "https://localhost/identity",
                ["Identity:Oidc:AllowDevelopmentEphemeralSigningKey"] = "true",
                ["Identity:Oidc:Clients:0:ClientId"] = "integration-oidc-client",
                ["Identity:Oidc:Clients:0:RedirectUris:0"] = "https://localhost/signin-oidc",
            });

        await IdentityOidcStoreAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Oidc_session_validates_authority_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            IdentityOidcSessionAssertions.Settings);

        await IdentityOidcSessionAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Oidc_protocol_authorization_flow_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            IdentityOidcProtocolAssertions.Settings);

        await IdentityOidcProtocolAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Oidc_client_management_admin_api_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            IdentityOidcProtocolAssertions.Settings);

        await IdentityOidcClientManagementAssertions.VerifyAsync(factory);
    }
}
