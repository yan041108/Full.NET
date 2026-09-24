using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcAgentRunLifecycleAssertions
{
    public static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var settings = Ai.AiAgentRuntimeTestSettings.ForIntegrationTests();
        foreach (var pair in IdentityOidcProtocolAssertions.Settings)
        {
            settings.Add(pair.Key, pair.Value);
        }

        using var factory = new FullNetApiFactory(
            provider,
            connectionString,
            settings);
        await factory.InitializeAsync(cancellationToken);
        await VerifyOidcSessionCanCreateAgentRunAsync(factory, cancellationToken);
        await VerifyRevokedOidcSessionCannotResumeAgentRunAsync(factory, cancellationToken);
    }

    private static async Task VerifyOidcSessionCanCreateAgentRunAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var client = factory.CreateClientForHost("localhost");
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);

        var modelId = await CreateModelConfigAsync(client, cancellationToken);
        await Ai.AiAgentRunApiAssertions.SeedFreshWorkerHeartbeatAsync(factory);
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/agent/runs")
        {
            Content = JsonContent.Create(new CreateAiAgentRunRequest(
                Guid.CreateVersion7(),
                "fullnet-single-text-v1",
                modelId,
                "oidc integration prompt",
                100,
                100)),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", flow.AccessToken);
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Accepted,
            createResponse.StatusCode,
            await createResponse.Content.ReadAsStringAsync(cancellationToken));
    }

    private static async Task VerifyRevokedOidcSessionCannotResumeAgentRunAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var client = factory.CreateClientForHost("localhost");
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);

        var modelId = await CreateModelConfigAsync(client, cancellationToken);
        await Ai.AiAgentRunApiAssertions.SeedFreshWorkerHeartbeatAsync(factory);
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/agent/runs")
        {
            Content = JsonContent.Create(new CreateAiAgentRunRequest(
                Guid.CreateVersion7(),
                "fullnet-single-text-v1",
                modelId,
                "oidc resume rejection prompt",
                100,
                100)),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", flow.AccessToken);
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Accepted, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateAiAgentRunResponse>(cancellationToken);
        Assert.IsNotNull(created);

        await SetRunStatusAsync(factory, created.RunId, "awaiting_approval", cancellationToken);

        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var sessionId = await ResolveRunSessionIdAsync(
            factory,
            created.RunId,
            cancellationToken);

        using var revokeRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/identity/online-sessions/{sessionId:D}/revoke")
        {
            Content = JsonContent.Create(new { }),
        };
        revokeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var revokeResponse = await client.SendAsync(revokeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, revokeResponse.StatusCode);

        using var resumeRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/ai/agent/runs/{created.RunId}/resume");
        resumeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", flow.AccessToken);
        using var resumeResponse = await client.SendAsync(resumeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, resumeResponse.StatusCode);
    }

    private static async Task<Guid> CreateModelConfigAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        using var modelRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/model-configs")
        {
            Content = JsonContent.Create(new CreateAiModelConfigRequest(
                null,
                "OIDC Agent Model",
                "ollama",
                "https://provider.test",
                "model",
                null,
                null,
                false,
                true)),
        };
        modelRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var modelResponse = await client.SendAsync(modelRequest, cancellationToken);
        Assert.IsTrue(modelResponse.IsSuccessStatusCode, await modelResponse.Content.ReadAsStringAsync(cancellationToken));
        var model = await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>(cancellationToken);
        Assert.IsNotNull(model);
        return model.Id;
    }

    private static async Task<Guid> ResolveRunSessionIdAsync(
        FullNetApiFactory factory,
        Guid runId,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = factory.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(factory.ConnectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(
                factory.ConnectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        return await connection.QuerySingleAsync<Guid>(
            new CommandDefinition(
                "SELECT SessionId FROM fn_ai_agent_run WHERE Id = @RunId",
                new { RunId = runId },
                cancellationToken: cancellationToken));
    }

    private static async Task SetRunStatusAsync(
        FullNetApiFactory factory,
        Guid runId,
        string statusKey,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = factory.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(factory.ConnectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(
                factory.ConnectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        await connection.ExecuteAsync(
            new CommandDefinition(
                "UPDATE fn_ai_agent_run SET StatusKey = @StatusKey WHERE Id = @RunId",
                new { RunId = runId, StatusKey = statusKey },
                cancellationToken: cancellationToken));
    }
}
