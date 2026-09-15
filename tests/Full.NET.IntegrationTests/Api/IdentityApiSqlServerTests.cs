using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Identity;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class IdentityApiSqlServerTests
{
    [TestMethod]
    public async Task Login_and_current_user_follow_secure_http_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await IdentityApiAssertions.VerifyLoginAsync(factory);
    }

    [TestMethod]
    public async Task Locale_preference_is_persisted_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await LocalePreferenceTests.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Last_super_administrator_is_protected_under_sql_server_concurrency()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await SuperAdministratorConcurrencyAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Session_refresh_and_context_switch_races_are_linearized()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await SessionRaceAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_user_management_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await IdentityUserManagementAssertions.VerifyHostUserManagementContractAsync(factory);
    }

    [TestMethod]
    public async Task Host_user_roles_follow_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await IdentityUserRolesManagementAssertions.VerifyHostUserRolesContractAsync(factory);
    }

    [TestMethod]
    public async Task Host_role_management_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await IdentityRoleManagementAssertions.VerifyHostRoleManagementContractAsync(factory);
    }

    [TestMethod]
    public async Task Host_role_data_scope_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await IdentityRoleDataScopeManagementAssertions.VerifyHostRoleDataScopeContractAsync(factory);
    }

    [TestMethod]
    public async Task Host_role_field_grants_enforce_projection_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await IdentityRoleFieldGrantAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_menu_management_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await IdentityMenuManagementAssertions.VerifyHostMenuManagementContractAsync(factory);
    }

    [TestMethod]
    public async Task Host_online_sessions_follow_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await IdentityOnlineSessionAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Single_session_policy_revokes_previous_login_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            new Dictionary<string, string?>
            {
                ["Identity:SessionLoginPolicy"] = nameof(IdentitySessionLoginPolicy.SingleSession),
            });

        await IdentityOnlineSessionAssertions.VerifySingleSessionPolicyAsync(factory);
    }

    [TestMethod]
    public async Task Host_api_keys_follow_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await IdentityApiKeyAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_open_access_clients_follow_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await IdentityOpenAccessClientAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_signature_authentication_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await IdentitySignatureAuthenticationAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_module_catalog_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await IdentityModuleCatalogAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Oidc_stores_enforce_redemption_and_revocation_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
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
    public async Task Oidc_session_validates_authority_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            IdentityOidcSessionAssertions.Settings);

        await IdentityOidcSessionAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Oidc_protocol_authorization_flow_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            IdentityOidcProtocolAssertions.Settings);

        await IdentityOidcProtocolAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Oidc_client_management_admin_api_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            IdentityOidcProtocolAssertions.Settings);

        await IdentityOidcClientManagementAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Oidc_authorization_management_admin_api_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            IdentityOidcProtocolAssertions.Settings);

        await IdentityOidcAuthorizationManagementAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Oidc_governance_scenarios_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            IdentityOidcProtocolAssertions.Settings);

        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        await IdentityOidcGovernanceAssertions.VerifyAsync(client);
    }

    [TestMethod]
    public async Task Oidc_retention_prunes_stale_grants_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            IdentityOidcRetentionAssertions.Settings);

        await IdentityOidcRetentionAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Oidc_logout_propagation_revokes_grants_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            IdentityOidcProtocolAssertions.Settings);

        await IdentityOidcLogoutPropagationAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Oidc_signing_key_rotation_preserves_validation_window_with_sql_server()
    {
        await IdentityOidcSigningKeyRotationAssertions.VerifyAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_multi_instance_governance_rejects_disabled_client_with_sql_server()
    {
        await IdentityOidcMultiInstanceGovernanceAssertions.VerifyAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_revoke_succeeds_when_realtime_publish_fails_with_sql_server()
    {
        await IdentityOidcRevokeRealtimeFaultAssertions.VerifyAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_signing_key_management_admin_api_with_sql_server()
    {
        await IdentityOidcSigningKeyManagementAssertions.VerifyAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_multi_instance_application_logout_propagates_with_sql_server()
    {
        await IdentityOidcMultiInstanceLogoutPropagationAssertions.VerifyAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }
}
