using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Full.NET.Abstractions.Ids;
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
        var registrationWayId = await ResolveRegistrationWayIdAsync(
            scopedFactory,
            factory.Provider,
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
                       invitationId)),
               })
        using (var challengeResponse = await client.SendAsync(challengeRequest, cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, challengeResponse.StatusCode);
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

    private static async Task<Guid> ResolveRegistrationWayIdAsync(
        FullNetApiFactory factory,
        DatabaseProvider provider,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var sql = provider == DatabaseProvider.MySql
            ? """
              SELECT Id
              FROM fn_identity_user_registration_way
              WHERE TenantId = @TenantId
              ORDER BY SortOrder, Name, Id
              LIMIT 1
              """
            : """
              SELECT TOP (1) Id
              FROM fn_identity_user_registration_way
              WHERE TenantId = @TenantId
              ORDER BY SortOrder, Name, Id
              """;
        var registrationWayId = await executor.QuerySingleOrDefaultAsync<Guid?>(
            new SqlStatement("integration.find_registration_way", sql, SqlDataScope.Global),
            new { TenantId = tenantId },
            cancellationToken);
        Assert.IsNotNull(registrationWayId);
        return registrationWayId.Value;
    }
}
