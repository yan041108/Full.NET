using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.RegistrationInvitations;
using Full.NET.Modules.Identity.Features.ManageRegistrationPolicy;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

internal static class InvitedRegistrationAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        var deliveryPort = new CapturingIdentityChallengeDeliveryPort();
        using var scopedFactory = new FullNetApiFactory(
            factory.Provider,
            factory.ConnectionString,
            configureTestServices: services =>
            {
                services.AddSingleton<IIdentityChallengeDeliveryPort>(deliveryPort);
            });
        await scopedFactory.InitializeAsync(cancellationToken);
        using var client = scopedFactory.CreateClientForHost("localhost");

        var tenantId = await ResolveDefaultTenantIdAsync(scopedFactory, factory.Provider, cancellationToken);
        var registrationWayId = await CreateRegistrationWayAsync(
            scopedFactory,
            tenantId,
            cancellationToken);
        var email = $"invite-{Guid.NewGuid():N}@example.com";
        var (invitationId, invitationToken) = await CreateInvitationAsync(
            scopedFactory,
            tenantId,
            email,
            registrationWayId,
            cancellationToken);

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
        var registration = new RegisterAccountRequest(email, "Invited User", "FullNet!2026Register",
            deliveryPort.LastIntent.ChallengeId, deliveryPort.LastIntent.Credential,
            InvitationId: invitationId, InvitationToken: invitationToken);
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
            Content = JsonContent.Create(new RegisterAccountRequest(
                email,
                "Invited User",
                "FullNet!2026Register",
                deliveryPort.LastIntent!.ChallengeId,
                deliveryPort.LastIntent.Credential,
                InvitationId: invitationId,
                InvitationToken: invitationToken)),
        };
        using var registerResponse = await client.SendAsync(registerRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, registerResponse.StatusCode);
        var body = await registerResponse.Content.ReadFromJsonAsync<RegisterAccountResponse>(cancellationToken);
        Assert.IsTrue(body!.Created);

        // 同一真实入口还覆盖开放注册，避免只验证邀请模式的特殊分支。
        await using (var scope = scopedFactory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            context.SetHost();
            try
            {
                var policy = scope.ServiceProvider.GetRequiredService<RegistrationPolicyService>();
                var current = await policy.GetAsync(cancellationToken);
                Assert.IsTrue(current.IsSuccess);
                Assert.IsTrue((await policy.UpdateAsync(new UpdateRegistrationPolicyRequest(true, current.Value!.Version,
                    IdentityRegistrationMode.Open), cancellationToken)).IsSuccess);
            }
            finally { context.Clear(); }
        }
        var openEmail = $"open-{Guid.NewGuid():N}@example.com";
        using (var challenge = await client.PostAsJsonAsync("/api/v1/auth/register/email-challenge",
            new SendRegistrationEmailChallengeRequest(openEmail, IdentityAccountChallengePurpose.RegistrationEmailVerification), cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, challenge.StatusCode);
        }
        registration = new RegisterAccountRequest(openEmail, "Open User", "FullNet!2026Register",
            deliveryPort.LastIntent!.ChallengeId, deliveryPort.LastIntent.Credential, registrationWayId);
        await VerifyFailedAttemptsAsync(scopedFactory, client, registration, cancellationToken);
        using (var resend = await client.PostAsJsonAsync("/api/v1/auth/register/email-challenge",
            new SendRegistrationEmailChallengeRequest(openEmail, IdentityAccountChallengePurpose.RegistrationEmailVerification), cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, resend.StatusCode);
        }
        registration = registration with { ChallengeId = deliveryPort.LastIntent!.ChallengeId, ChallengeCode = deliveryPort.LastIntent.Credential };
        await VerifyBusinessFailureRollbackAsync(scopedFactory, client, registration, cancellationToken);
        using var registered = await client.PostAsJsonAsync("/api/v1/auth/register", registration, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, registered.StatusCode);
        Assert.IsTrue((await registered.Content.ReadFromJsonAsync<RegisterAccountResponse>(cancellationToken))!.Created);
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

    private static async Task<(Guid InvitationId, string Token)> CreateInvitationAsync(
        FullNetApiFactory factory,
        Guid tenantId,
        string email,
        Guid registrationWayId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var idGenerator = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var commandExecutor = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var invitationId = idGenerator.NewId();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                RegistrationInvitationSql.Insert,
                IdentitySqlParameters.Create(
                    ("InvitationId", invitationId),
                    ("TenantId", tenantId),
                    ("NormalizedEmail", email),
                    ("CredentialHash", RegistrationInvitationCredentialHasher.Hash(invitationId, token)),
                    ("RegistrationWayId", registrationWayId),
                    ("Status", (byte)IdentityRegistrationInvitationStatus.Pending),
                    ("ExpiresAtUtc", now.AddDays(7)),
                    ("CreatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        return (invitationId, token);
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

    private static async Task<Guid> CreateRegistrationWayAsync(
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
