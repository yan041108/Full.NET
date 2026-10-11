using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.NativeAot;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Document;

/// <summary>共用真实 JIT 与 Native HTTP 场景，验证保留策略版本凭据及 Host 权限边界。</summary>
internal static class DocumentVersionRetentionAssertions
{
    private const string Path = "/api/v1/document/host/version-retention";

    internal static async Task VerifyHostAsync(HttpClient client, string token,
        string? logPath = null, CancellationToken cancellationToken = default)
    {
        using (var anonymousRead = await SendAsync(client, HttpMethod.Get, null, null, cancellationToken))
        {
            await StatusAsync(anonymousRead, HttpStatusCode.Unauthorized);
        }
        using (var anonymousWrite = await SendAsync(client, HttpMethod.Put, Request(0), null, cancellationToken))
        {
            await StatusAsync(anonymousWrite, HttpStatusCode.Unauthorized);
        }

        var username = $"retention-no-permission-{Guid.NewGuid():N}";
        using (var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identity/users")
            { Content = JsonContent.Create(new CreateHostUserRequest(username, "保留策略无权限用户", FullNetApiFactory.TestPassword)) })
        {
            create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var created = await client.SendAsync(create, cancellationToken);
            await StatusAsync(created, HttpStatusCode.Created);
        }
        // 正常完成首次改密，确保 403 来自精确权限策略，而非改密中间件。
        var deniedToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, username, cancellationToken: cancellationToken);
        await VerifyDeniedAsync(client, deniedToken, logPath, cancellationToken);

        var initial = await ReadAsync();
        Assert.AreEqual(0L, initial.Version, "场景必须使用无覆盖的新建测试库。");
        // 一个请求省略版本，另一个显式提供 0；无论调度先后都只有一个初始化成功。
        var legacy = new { initial.MinimumRetainedVersionsPerItem, initial.MaximumRetainedHistoryVersions, initial.PollSeconds, BatchSize = 51 };
        var responses = await Task.WhenAll(
            SendAsync(client, HttpMethod.Put, legacy, token, cancellationToken),
            SendAsync(client, HttpMethod.Put, Request(0), token, cancellationToken));
        using var first = responses[0];
        using var second = responses[1];
        Assert.AreEqual(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.AreEqual(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        var winningResponse = responses.Single(response => response.StatusCode == HttpStatusCode.OK);
        var winner = await winningResponse.Content.ReadFromJsonAsync<HostDocumentVersionRetentionSettingsResponse>(cancellationToken);
        Assert.IsNotNull(winner);
        Assert.AreEqual(1L, winner.Version);
        Assert.AreEqual(winner, await ReadAsync());
        await ConflictAsync(responses.Single(response => response.StatusCode == HttpStatusCode.Conflict));

        using (var staleInitial = await SendAsync(client, HttpMethod.Put, legacy, token, cancellationToken))
        {
            await ConflictAsync(staleInitial);
        }
        var update = Request(winner.Version) with { BatchSize = 75 };
        using (var saved = await SendAsync(client, HttpMethod.Put, update, token, cancellationToken))
        {
            await StatusAsync(saved, HttpStatusCode.OK);
            var committed = await saved.Content.ReadFromJsonAsync<HostDocumentVersionRetentionSettingsResponse>(cancellationToken);
            Assert.IsNotNull(committed);
            Assert.AreEqual(2L, committed.Version);
            Assert.AreEqual(75, committed.BatchSize);
            Assert.AreEqual(committed, await ReadAsync());
        }
        using (var stale = await SendAsync(client, HttpMethod.Put, update with { BatchSize = 99 }, token, cancellationToken))
        {
            await ConflictAsync(stale);
        }
        foreach (var invalid in new[] { -1L, long.MaxValue })
        {
            using var rejected = await SendAsync(client, HttpMethod.Put, Request(invalid), token, cancellationToken);
            await StatusAsync(rejected, HttpStatusCode.BadRequest);
        }
        var racingUpdates = await Task.WhenAll(
            SendAsync(client, HttpMethod.Put, Request(2) with { BatchSize = 80 }, token, cancellationToken),
            SendAsync(client, HttpMethod.Put, Request(2) with { BatchSize = 90 }, token, cancellationToken));
        using var racingFirst = racingUpdates[0];
        using var racingSecond = racingUpdates[1];
        Assert.AreEqual(1, racingUpdates.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.AreEqual(1, racingUpdates.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        await ConflictAsync(racingUpdates.Single(response => response.StatusCode == HttpStatusCode.Conflict));
        var racingWinner = await racingUpdates.Single(response => response.StatusCode == HttpStatusCode.OK)
            .Content.ReadFromJsonAsync<HostDocumentVersionRetentionSettingsResponse>(cancellationToken);
        Assert.IsNotNull(racingWinner);
        Assert.AreEqual(3L, racingWinner.Version);
        var unchanged = await ReadAsync();
        Assert.AreEqual(racingWinner, unchanged);

        async Task<HostDocumentVersionRetentionSettingsResponse> ReadAsync()
        {
            using var response = await SendAsync(client, HttpMethod.Get, null, token, cancellationToken);
            await StatusAsync(response, HttpStatusCode.OK);
            var settings = await response.Content.ReadFromJsonAsync<HostDocumentVersionRetentionSettingsResponse>(cancellationToken);
            Assert.IsNotNull(settings);
            return settings;
        }

        async Task ConflictAsync(HttpResponseMessage response)
        {
            await StatusAsync(response, HttpStatusCode.Conflict);
            Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual("document.version_retention.conflict", problem.RootElement.GetProperty("code").GetString());
        }

        Task StatusAsync(HttpResponseMessage response, HttpStatusCode status) =>
            NativeApiE2EAssertions.AssertStatusAsync(response, status, "Document retention CAS", cancellationToken, logPath);
    }

    internal static async Task VerifyDeniedAsync(HttpClient client, string token,
        string? logPath = null, CancellationToken cancellationToken = default)
    {
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put })
        {
            using var response = await SendAsync(client, method, method == HttpMethod.Put ? Request(2) : null, token, cancellationToken);
            await NativeApiE2EAssertions.AssertStatusAsync(response, HttpStatusCode.Forbidden,
                "Deny non-Host or unprivileged document retention", cancellationToken, logPath);
        }
    }

    internal static async Task VerifyJitAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        var token = await NativeApiE2EAssertions.LoginAsync(client);
        await VerifyHostAsync(client, token);
        await DocumentVersionRetentionRefreshAssertions.VerifyIndependentMonitorAsync(factory, client, token);
        using var availableRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenancy/available");
        availableRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var available = await client.SendAsync(availableRequest);
        Assert.AreEqual(HttpStatusCode.OK, available.StatusCode);
        var tenants = await available.Content.ReadFromJsonAsync<TenantContextSummary[]>();
        Assert.IsNotNull(tenants);
        using var enter = new HttpRequestMessage(HttpMethod.Put, "/api/v1/tenancy/context")
            { Content = JsonContent.Create(new ChangeTenantContextRequest(tenants.Single(tenant => tenant.Identifier == "acme").Id)) };
        enter.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var entered = await client.SendAsync(enter);
        Assert.AreEqual(HttpStatusCode.OK, entered.StatusCode);
        var tenantToken = await entered.Content.ReadFromJsonAsync<TenantContextTokenResponse>();
        Assert.IsNotNull(tenantToken);
        await VerifyDeniedAsync(client, tenantToken.AccessToken);
        var exportDirectory = Environment.GetEnvironmentVariable("FULLNET_DOCUMENT_RETENTION_OPENAPI_DIR");
        if (!string.IsNullOrWhiteSpace(exportDirectory))
        {
            // 在本批已经初始化的双库应用上导出权威契约，避免为导出再迁移两个空库。
            await OpenApiDocumentationAssertions.VerifyAsync(client);
            await OpenApiClientSnapshotContractAssertions.VerifyAsync(client, factory.Provider,
                exportPathOverride: System.IO.Path.Combine(exportDirectory, factory.Provider + ".raw.json"));
        }
    }

    private static UpdateHostDocumentVersionRetentionRequest Request(long version) => new(1, 0, 300, 50) { Version = version };

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method,
        object? body, string? token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, Path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
        return await client.SendAsync(request, cancellationToken);
    }
}
