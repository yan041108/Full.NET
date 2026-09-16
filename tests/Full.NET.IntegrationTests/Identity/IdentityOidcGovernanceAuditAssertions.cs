using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Microsoft.Extensions.DependencyInjection;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcGovernanceAuditAssertions
{
    private const string RedirectUri = "https://localhost:5013/signin-oidc-audit";

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var clientId = $"audit-{Guid.NewGuid():N}"[..24];
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identity/oidc-clients")
        {
            Content = JsonContent.Create(new CreateOidcClientRequest(
                clientId,
                "Audit test client",
                [RedirectUri],
                [],
                ["openid", "profile"],
                false,
                true,
                null)),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateOidcClientResponse>(cancellationToken);
        Assert.IsNotNull(created);

        var createAudit = await ReadLatestAuditAsync(
            factory,
            OidcManagementAuditWriter.ClientCreatedEventType,
            cancellationToken);
        Assert.IsNotNull(createAudit);
        StringAssert.Contains(createAudit!, clientId);

        using var disableRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/identity/oidc-clients/{created!.Client.Id:D}/disable");
        disableRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);

        var disableAudit = await ReadLatestAuditAsync(
            factory,
            OidcManagementAuditWriter.ClientDisabledEventType,
            cancellationToken);
        Assert.IsNotNull(disableAudit);
        StringAssert.Contains(disableAudit!, clientId);
    }

    private static async Task<string?> ReadLatestAuditAsync(
        FullNetApiFactory factory,
        string eventType,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sql = factory.Provider == DatabaseProvider.SqlServer
            ? """
              SELECT TOP (1) CONCAT(EventType, '|', UsernameFingerprint, '|', ResultCode)
              FROM fn_identity_auth_audit
              WHERE EventType = @EventType
              ORDER BY OccurredAtUtc DESC, Id DESC
              """
            : """
              SELECT CONCAT(EventType, '|', UsernameFingerprint, '|', ResultCode)
              FROM fn_identity_auth_audit
              WHERE EventType = @EventType
              ORDER BY OccurredAtUtc DESC, Id DESC
              LIMIT 1
              """;
        return await scope.ServiceProvider
            .GetRequiredService<IQueryExecutor>()
            .QuerySingleOrDefaultAsync<string>(
                new SqlStatement(
                    "integration.identity.read_latest_oidc_management_audit",
                    sql,
                    SqlDataScope.Global),
                new { EventType = eventType },
                cancellationToken);
    }
}