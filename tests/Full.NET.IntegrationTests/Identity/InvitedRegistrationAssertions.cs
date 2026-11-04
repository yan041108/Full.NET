using System.Text.Json;
using Full.NET.Abstractions.Results;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.RegistrationInvitations;
using Full.NET.Modules.Identity.Features.ManageRegistrationPolicy;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Features.AcceptTenantInvitation;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Identity.Oidc;
using OpenIddict.Abstractions;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>邀请注册：令牌消费与席位配额。</summary>
internal static class InvitedRegistrationAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default,
        IdentityRegistrationMode mode = IdentityRegistrationMode.InvitationOnly)
    {
        var deliveryPort = new CapturingIdentityChallengeDeliveryPort();
        using var scopedFactory = new FullNetApiFactory(
            factory.Provider,
            factory.ConnectionString,
            IdentityOidcProtocolAssertions.Settings,
            configureTestServices: services =>
            {
                services.AddSingleton<IIdentityChallengeDeliveryPort>(deliveryPort);
            });
        await scopedFactory.InitializeAsync(cancellationToken);
        using var client = scopedFactory.CreateClientForHost("localhost");

        await using (var policyScope = scopedFactory.Services.CreateAsyncScope())
        {
            policyScope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var policyService = policyScope.ServiceProvider.GetRequiredService<RegistrationPolicyService>();
            var policy = await policyService.GetAsync(cancellationToken);
            Assert.IsTrue(policy.IsSuccess);
            var changed = await policyService.UpdateAsync(new UpdateRegistrationPolicyRequest(
                mode == IdentityRegistrationMode.Open, policy.Value!.Version, mode), cancellationToken);
            Assert.IsTrue(changed.IsSuccess, changed.Error?.Code);
        }

        var tenantId = await ResolveDefaultTenantIdAsync(scopedFactory, factory.Provider, cancellationToken);
        var registrationWayId = await CreateRegistrationWayAsync(
            scopedFactory,
            tenantId,
            cancellationToken);
        await RegistrationAssignmentAssertions.PrepareAsync(scopedFactory, registrationWayId, tenantId, cancellationToken);
        var email = $"invite-{Guid.NewGuid():N}@example.com";
        var (invitationId, invitationToken) = await CreateInvitationAsync(
            scopedFactory,
            tenantId,
            email,
            registrationWayId,
            cancellationToken);
        await using (var proofScope = scopedFactory.Services.CreateAsyncScope())
        {
            proofScope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            Assert.IsTrue((await proofScope.ServiceProvider.GetRequiredService<RegistrationInvitationService>()
                .VerifyAsync(invitationId, invitationToken, cancellationToken)).IsSuccess);
        }

        using (var verifyRequest = new HttpRequestMessage(
                   HttpMethod.Post,
                   "/api/v1/auth/invitations/verify")
               {
                   Content = JsonContent.Create(new VerifyRegistrationInvitationRequest(
                       invitationId,
                       invitationToken)),
               })
        using (var verifyResponse = await client.SendAsync(verifyRequest, cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, verifyResponse.StatusCode);
        }

        using (var challengeRequest = new HttpRequestMessage(
                   HttpMethod.Post,
                   "/api/v1/auth/register/email-challenge")
               {
                   Content = JsonContent.Create(new SendRegistrationEmailChallengeRequest(
                       email,
                       IdentityAccountChallengePurpose.InvitationEmailVerification,
                       invitationId,
                       invitationToken)),
               })
        using (var challengeResponse = await client.SendAsync(challengeRequest, cancellationToken))
        {
            Assert.AreEqual(
                HttpStatusCode.OK,
                challengeResponse.StatusCode,
                await challengeResponse.Content.ReadAsStringAsync(cancellationToken));
        }

        Assert.IsNotNull(deliveryPort.LastIntent);
        var registration = new RegisterAccountRequest(
            email, "Invited User", "FullNet!2026Register", deliveryPort.LastIntent!.ChallengeId,
            deliveryPort.LastIntent.Credential, InvitationId: invitationId, InvitationToken: invitationToken);
        await VerifyDisabledPolicyAsync(scopedFactory, client, registration, cancellationToken);
        await VerifyFailedAttemptsAsync(scopedFactory, client, registration, cancellationToken);
        // 耗尽后显式重发新挑战，邀请不应因错误验证码被消费。
        using (var resend = await client.PostAsJsonAsync("/api/v1/auth/register/email-challenge",
            new SendRegistrationEmailChallengeRequest(email, IdentityAccountChallengePurpose.InvitationEmailVerification,
                invitationId, invitationToken), cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, resend.StatusCode);
        }
        registration = registration with { ChallengeId = deliveryPort.LastIntent!.ChallengeId, ChallengeCode = deliveryPort.LastIntent.Credential };
        await VerifyBusinessFailureRollbackAsync(scopedFactory, client, registration, cancellationToken);
        using var registerRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/register")
        {
            Content = JsonContent.Create(registration),
        };
        using var registerResponse = await client.SendAsync(registerRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, registerResponse.StatusCode);
        var body = await registerResponse.Content.ReadFromJsonAsync<RegisterAccountResponse>(cancellationToken);
        Assert.IsTrue(body!.Created);
        await using (var scope = scopedFactory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var invitation = await query.QuerySingleOrDefaultAsync<RegistrationInvitationRecord>(
                RegistrationInvitationSql.FindById, IdentitySqlParameters.Create(("InvitationId", invitationId)), cancellationToken);
            Assert.IsNotNull(invitation);
            Assert.AreEqual(body.UserId, invitation.BoundUserId);
            Assert.AreEqual((byte)IdentityRegistrationInvitationStatus.AccountCreatedPendingOnboarding, invitation.Status);
            Assert.IsNotNull(invitation.ConsumedAtUtc);
            var user = await query.QuerySingleOrDefaultAsync<IdentityUserRecord>(AccountLifecycleSql.FindUserByProfileEmail,
                IdentitySqlParameters.Create(("Email", email)), cancellationToken);
            Assert.IsNotNull(user);
            Assert.AreEqual(body.UserId, user.Id);
            Assert.IsNull(user.TenantId);
            Assert.AreEqual("host", user.ScopeKey);
            var parent = await query.QuerySingleOrDefaultAsync<TenantInvitationRecord>(TenantMembershipSql.FindInvitationByIdGlobal,
                IdentitySqlParameters.Create(("InvitationId", invitationId)), cancellationToken);
            Assert.AreEqual(body.UserId, parent!.TargetUserId);
            var snapshot = await scope.ServiceProvider.GetRequiredService<IPermissionSnapshotReader>().ReadAsync(body.UserId, "host", null, cancellationToken);
            CollectionAssert.AreEqual(new[] { "identity.navigation.read", "identity.tenant_registrations.continue_self", "identity.tenant_registrations.read_self", "tenancy.tenants.read", "tenancy.tenants.switch" }, snapshot.Permissions.ToArray());
        }
        using var replay = await client.PostAsJsonAsync("/api/v1/auth/register", registration, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, replay.StatusCode);
        var unboundInvitationId = await VerifyIssuanceBoundariesAsync(scopedFactory, tenantId, registrationWayId, email, cancellationToken);
        var accessToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, email, registration.Password, cancellationToken);
        ClaimsPrincipal centerPrincipal;
        var authorizationRequest = new OpenIddictRequest { ClientId = IdentityOidcRelyingPartyFixture.PublicClientId, Scope = "openid profile" };
        await using (var centerScope = scopedFactory.Services.CreateAsyncScope())
        {
            centerScope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var oidc = centerScope.ServiceProvider.GetRequiredService<IdentityOidcAuthorizationService>();
            var login = await oidc.SignInCenterAsync(email, registration.Password, cancellationToken);
            Assert.IsTrue(login.Succeeded);
            centerPrincipal = login.Principal!;
            Assert.IsNull(await oidc.ResolveTenantForConsentAsync(centerPrincipal, tenantId, cancellationToken));
            Assert.IsNull(await oidc.CreateAuthorizationPrincipalAsync(centerPrincipal, authorizationRequest, cancellationToken, tenantId));
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var beforeActivation = await client.GetFromJsonAsync<Full.NET.Modules.Tenancy.Contracts.TenantContextSummary[]>("/api/v1/tenancy/available", cancellationToken);
        Assert.AreEqual(0, beforeActivation!.Length);
        using (var forbiddenSwitch = await client.PutAsJsonAsync("/api/v1/tenancy/context",
            new ChangeTenantContextRequest(tenantId), cancellationToken))
            Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenSwitch.StatusCode);
        var pending = await client.GetFromJsonAsync<Full.NET.Abstractions.Results.PagedResult<MyTenantInvitationResponse>>("/api/v1/me/tenant-invitations", cancellationToken);
        Assert.IsTrue(pending!.Items.Any(item => item.Id == invitationId && item.Status == "Pending"));
        Assert.IsFalse(pending.Items.Any(item => item.Id == unboundInvitationId));
        using (var unproved = await client.PostAsync($"/api/v1/me/tenant-invitations/{unboundInvitationId:D}/accept", null, cancellationToken))
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, unproved.StatusCode);
        using var acceptedResponse = await client.PostAsync($"/api/v1/me/tenant-invitations/{invitationId:D}/accept", null, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, acceptedResponse.StatusCode, await acceptedResponse.Content.ReadAsStringAsync(cancellationToken));
        var accepted = await acceptedResponse.Content.ReadFromJsonAsync<AcceptTenantInvitationResponse>(cancellationToken);
        Assert.AreEqual("Active", accepted!.Status);
        Assert.AreEqual(tenantId, accepted.TenantId);
        await using (var activationScope = scopedFactory.Services.CreateAsyncScope())
        {
            activationScope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var row = await activationScope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<RegistrationInvitationRecord>(
                RegistrationInvitationSql.FindById, IdentitySqlParameters.Create(("InvitationId", invitationId)), cancellationToken);
            Assert.AreEqual((byte)IdentityRegistrationInvitationStatus.Activated, row!.Status);
            var oidc = activationScope.ServiceProvider.GetRequiredService<IdentityOidcAuthorizationService>();
            Assert.IsNotNull(await oidc.ResolveTenantForConsentAsync(centerPrincipal, tenantId, cancellationToken));
            Assert.IsNotNull(await oidc.CreateAuthorizationPrincipalAsync(centerPrincipal, authorizationRequest, cancellationToken, tenantId));
        }
        await RegistrationAssignmentAssertions.VerifyAsync(scopedFactory, registrationWayId, accepted, cancellationToken);
        _ = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, accessToken, tenantId, cancellationToken);
    }

    private static async Task VerifyFailedAttemptsAsync(FullNetApiFactory factory, HttpClient client,
        RegisterAccountRequest registration, CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/register",
                registration with { ChallengeCode = "incorrect-code" }, cancellationToken);
            await AssertProblemAsync(response, IdentityErrorCodes.AccountChallengeInvalid, cancellationToken);
            var record = await query.QuerySingleOrDefaultAsync<AccountChallengeRecord>(AccountChallengeSql.FindById,
                IdentitySqlParameters.Create(("ChallengeId", registration.ChallengeId)), cancellationToken);
            Assert.IsNotNull(record);
            Assert.AreEqual(attempt, record.AttemptCount, "注册失败不能回滚验证码错误次数。");
            Assert.IsNull(record.ConsumedAtUtc);
        }
        using var blocked = await client.PostAsJsonAsync("/api/v1/auth/register", registration, cancellationToken);
        await AssertProblemAsync(blocked, IdentityErrorCodes.AccountChallengeAttemptsExceeded, cancellationToken);
        Assert.IsNull(await query.QuerySingleOrDefaultAsync<IdentityUserRecord>(AccountLifecycleSql.FindUserByProfileEmail,
            IdentitySqlParameters.Create(("Email", registration.Email)), cancellationToken));
        if (registration.InvitationId.HasValue)
        {
            var invitation = await query.QuerySingleOrDefaultAsync<RegistrationInvitationRecord>(RegistrationInvitationSql.FindById,
                IdentitySqlParameters.Create(("InvitationId", registration.InvitationId.Value)), cancellationToken);
            Assert.IsNotNull(invitation);
            Assert.AreEqual((byte)IdentityRegistrationInvitationStatus.Pending, invitation.Status);
            Assert.IsNull(invitation.ConsumedAtUtc);
        }
    }

    private static async Task VerifyDisabledPolicyAsync(FullNetApiFactory factory, HttpClient client,
        RegisterAccountRequest registration, CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        context.SetHost();
        var policy = scope.ServiceProvider.GetRequiredService<RegistrationPolicyService>();
        var original = await policy.GetAsync(cancellationToken);
        Assert.IsTrue(original.IsSuccess);
        try
        {
            var disabled = await policy.UpdateAsync(new UpdateRegistrationPolicyRequest(false,
                original.Value!.Version, IdentityRegistrationMode.Disabled), cancellationToken);
            Assert.IsTrue(disabled.IsSuccess);
            Assert.AreEqual(IdentityRegistrationMode.Disabled, disabled.Value!.RegistrationMode);
            // 使用仍有效的邀请与验证码，证明关闭模式在真实入口拒绝注册而非凭据验证失败。
            using var register = await client.PostAsJsonAsync("/api/v1/auth/register", registration, cancellationToken);
            Assert.AreEqual(HttpStatusCode.Forbidden, register.StatusCode);
            await AssertProblemAsync(register, IdentityErrorCodes.RegistrationDisabled, cancellationToken, HttpStatusCode.Forbidden);
            using var challenge = await client.PostAsJsonAsync("/api/v1/auth/register/email-challenge",
                new SendRegistrationEmailChallengeRequest(registration.Email,
                    IdentityAccountChallengePurpose.InvitationEmailVerification,
                    registration.InvitationId, registration.InvitationToken), cancellationToken);
            Assert.AreEqual(HttpStatusCode.Forbidden, challenge.StatusCode);
            await AssertProblemAsync(challenge, IdentityErrorCodes.RegistrationDisabled, cancellationToken, HttpStatusCode.Forbidden);
        }
        finally
        {
            try
            {
                var current = await policy.GetAsync(cancellationToken);
                Assert.IsTrue(current.IsSuccess);
                Assert.IsTrue((await policy.UpdateAsync(new UpdateRegistrationPolicyRequest(
                    original.Value!.IsPublicRegistrationEnabled, current.Value!.Version,
                    original.Value.RegistrationMode), cancellationToken)).IsSuccess);
            }
            finally { context.Clear(); }
        }
    }

    private static async Task VerifyBusinessFailureRollbackAsync(FullNetApiFactory factory, HttpClient client,
        RegisterAccountRequest registration, CancellationToken cancellationToken)
    {
        // 非空弱密码通过请求验证后在业务策略失败，挑战消费必须随整个注册事务回滚。
        using var failed = await client.PostAsJsonAsync("/api/v1/auth/register", registration with { Password = "weak" }, cancellationToken);
        await AssertProblemAsync(failed, ValidationErrorCodes.Failed, cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var record = await query.QuerySingleOrDefaultAsync<AccountChallengeRecord>(AccountChallengeSql.FindById,
            IdentitySqlParameters.Create(("ChallengeId", registration.ChallengeId)), cancellationToken);
        Assert.IsNotNull(record);
        Assert.IsNull(record.ConsumedAtUtc);
        Assert.AreEqual(0, record.AttemptCount);
        Assert.AreEqual(1, record.Version);
        Assert.IsNull(await query.QuerySingleOrDefaultAsync<IdentityUserRecord>(AccountLifecycleSql.FindUserByProfileEmail,
            IdentitySqlParameters.Create(("Email", registration.Email)), cancellationToken));
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, string code, CancellationToken cancellationToken,
        HttpStatusCode expectedStatus = HttpStatusCode.BadRequest)
    {
        Assert.AreEqual(expectedStatus, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(code, document.RootElement.GetProperty("code").GetString());
    }

    private static async Task<Guid> VerifyIssuanceBoundariesAsync(FullNetApiFactory factory, Guid tenantId, Guid wayId, string email, CancellationToken token)
    {
        var revoked = await CreateInvitationAsync(factory, tenantId, email, wayId, token);
        var limited = await factory.CreateHostIdentityAsync($"grantor-{Guid.NewGuid():N}",
            [IdentityTenantMembershipPermissions.Invite, "tenancy.tenants.switch"], token);
        await using var scope = factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var current = sp.GetRequiredService<CurrentTenantAccessor>();
        current.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
        var service = sp.GetRequiredService<TenantMembershipManagementService>();
        Assert.IsTrue((await service.RevokeInvitationAsync(revoked.InvitationId, token)).IsSuccess);
        current.SetHost();
        var verifier = sp.GetRequiredService<RegistrationInvitationService>();
        Assert.IsFalse((await verifier.VerifyAsync(revoked.InvitationId, revoked.Token, token)).IsSuccess);
        var query = sp.GetRequiredService<IQueryExecutor>();
        Assert.AreEqual(3, await query.QuerySingleOrDefaultAsync<int>(new SqlStatement("integration.invitation.child_status",
            "SELECT Status FROM fn_identity_registration_invitation WHERE InvitationId = @Id", SqlDataScope.Global),
            IdentitySqlParameters.Create(("Id", revoked.InvitationId)), token));
        var count = new SqlStatement("integration.invitation.count", "SELECT COUNT(1) FROM fn_identity_tenant_invitation WHERE TenantId = @TenantId", SqlDataScope.Global);
        var parameters = IdentitySqlParameters.Create(("TenantId", tenantId));
        var before = await query.QuerySingleOrDefaultAsync<int>(count, parameters, token);
        current.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
        var invalid = await service.InviteAsync(limited.UserId, new CreateTenantInvitationRequest(email, "Member", RegistrationWayId: Guid.CreateVersion7()), token);
        Assert.IsFalse(invalid.IsSuccess);
        current.SetHost();
        Assert.AreEqual(before, await query.QuerySingleOrDefaultAsync<int>(count, parameters, token));
        var authority = sp.GetRequiredService<ITenantInvitationGrantorAuthority>();
        Assert.IsTrue(await authority.IsAuthorizedAsync(limited.UserId, tenantId, token));
        await sp.GetRequiredService<ICommandExecutor>().ExecuteAsync(new SqlStatement("integration.invitation.revoke_grant", """
            DELETE FROM fn_identity_role_permission WHERE PermissionCode = @Permission
              AND RoleId IN (SELECT RoleId FROM fn_identity_user_role WHERE UserId = @UserId)
            """, SqlDataScope.Global), IdentitySqlParameters.Create(("Permission", IdentityTenantMembershipPermissions.Invite), ("UserId", limited.UserId)), token);
        Assert.IsFalse(await authority.IsAuthorizedAsync(limited.UserId, tenantId, token));
        var commands = sp.GetRequiredService<ICommandExecutor>();
        var roleId = await query.QuerySingleOrDefaultAsync<Guid>(new SqlStatement("integration.invitation.grantor_role",
            "SELECT RoleId FROM fn_identity_user_role WHERE UserId = @UserId", SqlDataScope.Global), IdentitySqlParameters.Create(("UserId", limited.UserId)), token);
        await commands.ExecuteAsync(new SqlStatement("integration.invitation.revoke_platform_context", """
            DELETE FROM fn_identity_role_permission WHERE RoleId = @RoleId AND PermissionCode = 'tenancy.tenants.switch'
            """, SqlDataScope.Global), IdentitySqlParameters.Create(("RoleId", roleId)), token);
        await commands.ExecuteAsync(IdentitySql.EnsureRolePermission, IdentitySqlParameters.Create(("RoleId", roleId),
            ("PermissionCode", IdentityTenantMembershipPermissions.Invite)), token);
        current.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
        var memberId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        // 本夹具只准备邀请人归属，席位激活仍由本用例的新受邀账号走正式编排验证。
        await commands.ExecuteAsync(TenantMembershipSql.InsertMember, IdentitySqlParameters.Create(("Id", memberId), ("UserId", limited.UserId),
            ("MemberRole", "Member"), ("Status", "Active"), ("CreatedAtUtc", now), ("UpdatedAtUtc", now), ("Version", 1)), token);
        Assert.IsTrue(await authority.IsAuthorizedAsync(limited.UserId, tenantId, token));
        await commands.ExecuteAsync(TenantMembershipSql.UpdateMember, IdentitySqlParameters.Create(("MemberId", memberId), ("MemberRole", "Member"),
            ("Status", "Removed"), ("UpdatedAtUtc", now), ("Version", 1)), token);
        Assert.IsFalse(await authority.IsAuthorizedAsync(limited.UserId, tenantId, token));
        return (await CreateInvitationAsync(factory, tenantId, email, wayId, token)).InvitationId;
    }

    internal static async Task<(Guid InvitationId, string Token)> CreateInvitationAsync(
        FullNetApiFactory factory,
        Guid tenantId,
        string email,
        Guid registrationWayId,
        CancellationToken cancellationToken)
    {
        using var host = factory.CreateClientForHost("localhost");
        var admin = await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin", cancellationToken: cancellationToken);
        var tenantToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, admin, tenantId, cancellationToken);
        using var client = factory.CreateClientForHost("acme.localhost");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);
        var options = await client.GetFromJsonAsync<PublicRegistrationWayResponse[]>("/api/v1/identity/tenant-members/invitations/registration-ways", cancellationToken);
        Assert.IsTrue(options!.Any(item => item.Id == registrationWayId));
        using var response = await client.PostAsJsonAsync("/api/v1/identity/tenant-members/invitations",
            new CreateTenantInvitationRequest(email, "Member", RegistrationWayId: registrationWayId), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        var result = await response.Content.ReadFromJsonAsync<CreateTenantInvitationResult>(cancellationToken);
        Assert.AreEqual(result!.Invitation.Id, result.RegistrationInvitationId);
        return (result.Invitation.Id, result.InvitationToken);
    }

    private static async Task<Guid> ResolveDefaultTenantIdAsync(
        FullNetApiFactory factory,
        DatabaseProvider provider,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var sql = provider == DatabaseProvider.MySql
            ? "SELECT Id FROM fn_tenancy_tenant ORDER BY CreatedAtUtc, Id LIMIT 1"
            : "SELECT TOP (1) Id FROM fn_tenancy_tenant ORDER BY CreatedAtUtc, Id";
        var tenantId = await executor.QuerySingleOrDefaultAsync<Guid?>(
            new SqlStatement("integration.find_first_tenant", sql, SqlDataScope.Global),
            new { },
            cancellationToken);
        Assert.IsNotNull(tenantId);
        return tenantId.Value;
    }

    internal static async Task<Guid> CreateRegistrationWayAsync(
        FullNetApiFactory factory,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var idGenerator = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        var roleId = idGenerator.NewId();
        var unitId = idGenerator.NewId();
        var wayId = idGenerator.NewId();
        var suffix = wayId.ToString("N");

        try
        {
            // 受邀注册依赖有效注册方式；测试自行创建场景数据，不依赖环境 Overlay。
            currentTenant.SetHost();
            await command.ExecuteAsync(
                IdentitySql.InsertRole,
                new
                {
                    Id = roleId,
                    TenantId = (Guid?)tenantId,
                    ScopeKey = $"tenant:{tenantId:N}",
                    Code = $"invite-{suffix}",
                    Name = "Invited Registration Role",
                    IsSystem = false,
                    IsActive = true,
                    IsSuperAdministrator = false,
                    DataScopeKind = "all",
                    CreatedAtUtc = now,
                    Version = 1,
                },
                cancellationToken);

            currentTenant.SetTenant(new TenantContext(tenantId, "acme", "Acme Corporation"));
            await command.ExecuteAsync(
                new SqlStatement(
                    "integration.insert_invited_registration_unit",
                    """
                    INSERT INTO fn_organization_unit
                        (Id, TenantId, ParentId, Code, Name, DisplayOrder,
                         IsActive, CreatedAtUtc, UpdatedAtUtc, Version)
                    VALUES
                        (@Id, @TenantId, NULL, @Code, @Name, 0,
                         1, @CreatedAtUtc, NULL, 1)
                    """,
                    SqlDataScope.TenantRequired,
                    SqlTenantBinding.CurrentTenantId),
                new
                {
                    Id = unitId,
                    Code = $"invite-{suffix}",
                    Name = "Invited Registration Unit",
                    CreatedAtUtc = now,
                },
                cancellationToken);

            currentTenant.SetHost();
            await command.ExecuteAsync(
                RegistrationWaySql.Insert,
                new RegistrationWayRecord
                {
                    Id = wayId,
                    TenantId = tenantId,
                    Name = "Invited Registration",
                    Code = $"invite-{suffix}",
                    IsEnabled = true,
                    RoleId = roleId,
                    OrganizationUnitId = unitId,
                    SortOrder = 0,
                    CreatedAtUtc = now,
                    Version = 1,
                },
                cancellationToken);
            return wayId;
        }
        finally
        {
            currentTenant.Clear();
        }
    }
}
