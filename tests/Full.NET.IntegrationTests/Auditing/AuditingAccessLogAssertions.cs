using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Auditing;

/// <summary>
/// Host 访问日志纵向切片验收夹具。
/// </summary>
internal static class AuditingAccessLogAssertions
{
    private static readonly SqlStatement InsertCursorTieAccessLog = new(
        "test.insert_cursor_tie_access_log",
        """
        INSERT INTO fn_auditing_access_log
            (Id, OccurredAtUtc, HttpMethod, RequestPath, StatusCode, DurationMs,
             UserId, TenantId, TraceId, ClientIpFingerprint, IsAuthenticated)
        VALUES
            (@Id, @OccurredAtUtc, @HttpMethod, @RequestPath, @StatusCode, @DurationMs,
             @UserId, @TenantId, @TraceId, @ClientIpFingerprint, @IsAuthenticated)
        """,
        SqlDataScope.HostOnly);

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        await VerifyWriteAndQueryAsync(factory, client, cancellationToken);
        await OpenApiAuditingAccessLogsContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifyListRequiresReadPermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var token = await factory.CreateHostAccessTokenAsync(
            ["platform.dashboard.read"],
            cancellationToken);
        foreach (var path in new[]
                 {
                     "/api/v1/auditing/access-logs?page=1&pageSize=20",
                     "/api/v1/auditing/access-logs/cursor?limit=20",
                 })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                token);
            using var response = await client.SendAsync(request, cancellationToken);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
            using var problem = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual(
                "authorization.permission_denied",
                problem.RootElement.GetProperty("code").GetString());
        }
    }

    private static async Task VerifyWriteAndQueryAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);

        using var probeRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/settings/enum-catalogs");
        probeRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var probeResponse = await client.SendAsync(probeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, probeResponse.StatusCode);

        using var secondProbeRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/settings/enum-catalogs");
        secondProbeRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var secondProbeResponse = await client.SendAsync(
            secondProbeRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, secondProbeResponse.StatusCode);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/auditing/access-logs?page=1&pageSize=50&pathContains=/api/v1/settings/enum-catalogs");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content
            .ReadFromJsonAsync<PagedResult<AccessLogResponse>>(cancellationToken);
        Assert.IsNotNull(page);
        Assert.IsGreaterThan(0, page.Total);
        Assert.IsTrue(page.Items.Any(item =>
            item.RequestPath.Contains("/api/v1/settings/enum-catalogs", StringComparison.Ordinal)
            && string.Equals(item.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase)));

        await VerifyCursorQueryAsync(client, adminToken, cancellationToken);
        await VerifyCursorTieBoundaryAsync(
            factory,
            client,
            adminToken,
            cancellationToken);

        var first = page.Items[0];
        using var detailRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/auditing/access-logs/{first.Id:D}");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var detailResponse = await client.SendAsync(detailRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content
            .ReadFromJsonAsync<AccessLogResponse>(cancellationToken);
        Assert.IsNotNull(detail);
        Assert.AreEqual(first.Id, detail.Id);

        using var missingRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/auditing/access-logs/{Guid.CreateVersion7():D}");
        missingRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var missingResponse = await client.SendAsync(missingRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await missingResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            AuditingErrorCodes.AccessLogNotFound,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyCursorQueryAsync(
        HttpClient client,
        string adminToken,
        CancellationToken cancellationToken)
    {
        const string pathFilter = "/api/v1/settings/enum-catalogs";
        var encodedFilter = Uri.EscapeDataString(pathFilter);
        using var firstRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/auditing/access-logs/cursor?limit=1&pathContains={encodedFilter}");
        firstRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var firstResponse = await client.SendAsync(
            firstRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstPage = await firstResponse.Content
            .ReadFromJsonAsync<AccessLogCursorPageResponse>(cancellationToken);
        Assert.IsNotNull(firstPage);
        Assert.HasCount(1, firstPage.Items);
        Assert.IsTrue(firstPage.HasMore);
        Assert.IsFalse(string.IsNullOrWhiteSpace(firstPage.NextCursor));

        using var nextRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/auditing/access-logs/cursor?limit=1&pathContains={encodedFilter}&cursor={Uri.EscapeDataString(firstPage.NextCursor)}");
        nextRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var nextResponse = await client.SendAsync(
            nextRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, nextResponse.StatusCode);
        var nextPage = await nextResponse.Content
            .ReadFromJsonAsync<AccessLogCursorPageResponse>(cancellationToken);
        Assert.IsNotNull(nextPage);
        Assert.HasCount(1, nextPage.Items);
        Assert.AreNotEqual(firstPage.Items[0].Id, nextPage.Items[0].Id);

        using var mismatchedFilterRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/auditing/access-logs/cursor?limit=1&pathContains=%2Fapi%2Fv1%2Fidentity&cursor={Uri.EscapeDataString(firstPage.NextCursor)}");
        mismatchedFilterRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var mismatchedFilterResponse = await client.SendAsync(
            mismatchedFilterRequest,
            cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.BadRequest,
            mismatchedFilterResponse.StatusCode);
        using var mismatchedProblem = JsonDocument.Parse(
            await mismatchedFilterResponse.Content.ReadAsStringAsync(
                cancellationToken));
        Assert.AreEqual(
            AuditingErrorCodes.AccessLogCursorInvalid,
            mismatchedProblem.RootElement.GetProperty("code").GetString());

        using var invalidRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/auditing/access-logs/cursor?limit=1&cursor=invalid%21");
        invalidRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var invalidResponse = await client.SendAsync(
            invalidRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await invalidResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            AuditingErrorCodes.AccessLogCursorInvalid,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyCursorTieBoundaryAsync(
        FullNetApiFactory factory,
        HttpClient client,
        string adminToken,
        CancellationToken cancellationToken)
    {
        var pathFilter = $"/integration/cursor-tie/{Guid.NewGuid():N}";
        await SeedCursorTieRowsAsync(
            factory,
            pathFilter,
            cancellationToken);
        var encodedFilter = Uri.EscapeDataString(pathFilter);
        using var firstRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/auditing/access-logs/cursor?limit=2&pathContains={encodedFilter}");
        firstRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var firstResponse = await client.SendAsync(
            firstRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstPage = await firstResponse.Content
            .ReadFromJsonAsync<AccessLogCursorPageResponse>(cancellationToken);
        Assert.IsNotNull(firstPage);
        Assert.HasCount(2, firstPage.Items);
        Assert.IsTrue(firstPage.HasMore);
        Assert.IsFalse(string.IsNullOrWhiteSpace(firstPage.NextCursor));

        using var nextRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/auditing/access-logs/cursor?limit=2&pathContains={encodedFilter}&cursor={Uri.EscapeDataString(firstPage.NextCursor)}");
        nextRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var nextResponse = await client.SendAsync(
            nextRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, nextResponse.StatusCode);
        var nextPage = await nextResponse.Content
            .ReadFromJsonAsync<AccessLogCursorPageResponse>(cancellationToken);
        Assert.IsNotNull(nextPage);
        Assert.HasCount(1, nextPage.Items);
        Assert.IsFalse(nextPage.HasMore);

        var items = firstPage.Items.Concat(nextPage.Items).ToArray();
        Assert.AreEqual(3, items.Select(item => item.Id).Distinct().Count());
        Assert.AreEqual(1, items.Select(item => item.OccurredAtUtc).Distinct().Count());
    }

    private static async Task SeedCursorTieRowsAsync(
        FullNetApiFactory factory,
        string requestPath,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var occurredAtUtc = new DateTimeOffset(
            now.Ticks - (now.Ticks % 10),
            TimeSpan.Zero);
        await using var scope = factory.Services.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider
            .GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            var command = scope.ServiceProvider
                .GetRequiredService<ICommandExecutor>();
            for (var index = 0; index < 3; index++)
            {
                await command.ExecuteAsync(
                    InsertCursorTieAccessLog,
                    new
                    {
                        Id = Guid.CreateVersion7(),
                        OccurredAtUtc = occurredAtUtc,
                        HttpMethod = "GET",
                        RequestPath = requestPath,
                        StatusCode = 200,
                        DurationMs = index + 1,
                        UserId = (Guid?)null,
                        TenantId = (Guid?)null,
                        TraceId = (string?)null,
                        ClientIpFingerprint = (string?)null,
                        IsAuthenticated = false,
                    },
                    cancellationToken);
            }
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private static async Task<string> LoginAsHostAdminAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(token);
        return token.AccessToken;
    }
}
