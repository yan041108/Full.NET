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

    [TestMethod]
    public async Task Oidc_authorization_management_admin_api_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            IdentityOidcProtocolAssertions.Settings);

        await IdentityOidcAuthorizationManagementAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Oidc_governance_scenarios_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            IdentityOidcProtocolAssertions.Settings);

        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        await IdentityOidcGovernanceAssertions.VerifyAsync(client);
    }

    [TestMethod]
    public async Task Oidc_session_write_rejects_malicious_origin_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            IdentityOidcProtocolAssertions.Settings);

        await IdentityOidcSessionSecurityAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Oidc_userinfo_respects_profile_scope_with_mysql()
    {
        await IdentityOidcUserInfoScopeAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_endpoint_errors_do_not_leak_internals_with_mysql()
    {
        await IdentityOidcEndpointErrorBoundaryAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_retention_prunes_stale_grants_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            IdentityOidcRetentionAssertions.Settings);

        await IdentityOidcRetentionAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Oidc_logout_propagation_revokes_grants_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            IdentityOidcProtocolAssertions.Settings);

        await IdentityOidcLogoutPropagationAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Oidc_signing_key_rotation_preserves_validation_window_with_mysql()
    {
        await IdentityOidcSigningKeyRotationAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_signing_key_retirement_rejects_legacy_tokens_with_mysql()
    {
        await IdentityOidcSigningKeyRetirementAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_token_boundary_rejects_invalid_access_tokens_with_mysql()
    {
        await IdentityOidcTokenBoundaryAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_jwks_does_not_expose_private_key_material_with_mysql()
    {
        await IdentityOidcJwksBoundaryAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_external_client_tokens_omit_internal_claims_with_mysql()
    {
        await IdentityOidcTokenClaimBoundaryAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_external_client_api_permission_boundary_with_mysql()
    {
        await IdentityOidcApiPermissionBoundaryAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_tenant_boundary_rejects_forged_tenant_claim_with_mysql()
    {
        await IdentityOidcTenantBoundaryAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_multi_instance_governance_rejects_disabled_client_with_mysql()
    {
        await IdentityOidcMultiInstanceGovernanceAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_revoke_succeeds_when_realtime_publish_fails_with_mysql()
    {
        await IdentityOidcRevokeRealtimeFaultAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_signing_key_management_admin_api_with_mysql()
    {
        await IdentityOidcSigningKeyManagementAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_multi_instance_application_logout_propagates_with_mysql()
    {
        await IdentityOidcMultiInstanceLogoutPropagationAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_multi_instance_center_logout_propagates_with_mysql()
    {
        await IdentityOidcMultiInstanceCenterLogoutPropagationAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_multi_instance_signing_key_rotation_overlap_with_mysql()
    {
        await IdentityOidcMultiInstanceSigningKeyRotationAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_multi_instance_signing_key_activate_overlap_with_mysql()
    {
        await IdentityOidcMultiInstanceSigningKeyActivateAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_multi_instance_signing_key_retirement_rejects_legacy_tokens_with_mysql()
    {
        await IdentityOidcMultiInstanceSigningKeyRetirementAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_account_authority_invalidates_tokens_with_mysql()
    {
        await IdentityOidcAccountAuthorityAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_account_lockout_invalidates_protocol_grants_with_mysql()
    {
        await IdentityOidcAccountLockoutAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_center_session_revoke_invalidates_protocol_grants_with_mysql()
    {
        await IdentityOidcCenterSessionRevokeAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_client_disable_invalidates_protocol_grants_with_mysql()
    {
        await IdentityOidcClientDisableProtocolAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_must_change_password_invalidates_protocol_grants_with_mysql()
    {
        await IdentityOidcMustChangePasswordProtocolAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_revoke_is_idempotent_with_mysql()
    {
        await IdentityOidcRevokeIdempotencyAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_revoke_notifications_carry_authoritative_session_id_with_mysql()
    {
        await IdentityOidcRevokeNotificationAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_multi_instance_staggered_exchange_preserves_tokens_with_mysql()
    {
        await IdentityOidcMultiInstanceRestartAssertions.VerifyStaggeredExchangeAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_multi_instance_peer_validates_access_token_with_mysql()
    {
        await IdentityOidcMultiInstanceRestartAssertions.VerifyPeerInstanceValidatesIssuedAccessTokenAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_session_state_fault_fails_closed_with_mysql()
    {
        await IdentityOidcSessionStateFaultAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_protocol_endpoints_enforce_session_authority_with_mysql()
    {
        await IdentityOidcProtocolAuthorityAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_client_offline_authoritative_revoke_with_mysql()
    {
        await IdentityOidcClientOfflineAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_client_disable_emits_session_revoked_notifications_with_mysql()
    {
        await IdentityOidcClientDisableNotificationsAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_concurrent_refresh_reuse_detected_with_mysql()
    {
        await IdentityOidcRefreshRaceAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_refresh_preserves_application_session_binding_with_mysql()
    {
        await IdentityOidcRefreshLifecycleAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task Oidc_application_revoke_preserves_center_sso_for_other_apps_with_mysql()
    {
        await IdentityOidcApplicationRevokeCenterSsoAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }
}
