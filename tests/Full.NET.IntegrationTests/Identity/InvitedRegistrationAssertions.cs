using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.RegistrationInvitations;
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
