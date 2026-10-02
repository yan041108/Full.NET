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
using Full.NET.Modules.Settings.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Auditing;

/// <summary>
/// Host 操作日志纵向切片验收夹具。
/// </summary>
internal static class AuditingOperationLogAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        await VerifyWriteAndQueryAsync(client, cancellationToken);
        await VerifyRestrictedDetailsAsync(factory, client, cancellationToken);
        await OpenApiAuditingOperationLogsContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifyRestrictedDetailsAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var validId = Guid.CreateVersion7();
        var expiredId = Guid.CreateVersion7();
        var invalidId = Guid.CreateVersion7();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            tenant.SetHost();
            try
            {
                await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(
                    new SqlStatement(
                        "test.auditing.operation_details.insert_fixtures",
                        """
                        INSERT INTO fn_auditing_operation_log
                            (Id, OccurredAtUtc, ActionKey, HttpMethod, RequestPath,
                             StatusCode, DurationMs, Succeeded, UserId, TenantId,
                             TraceId, ClientIpFingerprint, PermissionCode, ContextJson,
                             DetailsExpiresAtUtc)
                        VALUES
                            (@ValidId, @NowUtc, 'details.valid', 'POST', '/details/valid',
                             200, 1, 1, NULL, NULL, NULL, NULL, NULL,
                             '{"schemaVersion":1,"clientIp":"203.0.113.7","clientPort":54321,"unapproved":"secret-marker"}', @FutureUtc),
                            (@ExpiredId, @NowUtc, 'details.expired', 'POST', '/details/expired',
                             200, 1, 1, NULL, NULL, NULL, NULL, NULL,
                             '{"schemaVersion":1,"clientIp":"203.0.113.8"}', @PastUtc),
                            (@InvalidId, @NowUtc, 'details.invalid', 'POST', '/details/invalid',
                             200, 1, 1, NULL, NULL, NULL, NULL, NULL,
                             '{"schemaVersion":2,"clientIp":"203.0.113.9"}', @FutureUtc)
                        """,
                        SqlDataScope.HostOnly),
                    new
                    {
                        ValidId = validId,
                        ExpiredId = expiredId,
                        InvalidId = invalidId,
                        NowUtc = now,
                        FutureUtc = now.AddHours(1),
                        PastUtc = now.AddMinutes(-1),
                    },
                    cancellationToken);
            }
            finally
            {
                tenant.Clear();
            }
        }

        var detailsPath = $"/api/v1/auditing/operation-logs/{validId:D}/details";
        using (var anonymous = await client.GetAsync(detailsPath, cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        var readOnly = await factory.CreateHostAccessTokenAsync(
            [OperationLogPermissions.Read], cancellationToken);
        var detailsOnly = await factory.CreateHostAccessTokenAsync(
            [OperationLogPermissions.ReadDetails], cancellationToken);
        var both = await factory.CreateHostAccessTokenAsync(
            [OperationLogPermissions.Read, OperationLogPermissions.ReadDetails],
            cancellationToken);
        await AssertDetailsStatusAsync(client, detailsPath, readOnly,
            HttpStatusCode.Forbidden, cancellationToken);
        await AssertDetailsStatusAsync(client, detailsPath, detailsOnly,
            HttpStatusCode.Forbidden, cancellationToken);

        using (var request = AuthorizedGet(detailsPath, both))
        using (var response = await client.SendAsync(request, cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            Assert.IsFalse(payload.Contains("secret-marker", StringComparison.Ordinal));
            var details = JsonSerializer.Deserialize<OperationLogDetailsResponse>(payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.IsNotNull(details);
            Assert.AreEqual(validId, details.Id);
            Assert.AreEqual("203.0.113.7", details.Context.ClientIp);
            Assert.AreEqual(54321, details.Context.ClientPort);
        }

        // 普通详情即使有读取权限，也不能携带受限上下文或原始 JSON。
        using (var request = AuthorizedGet(
            $"/api/v1/auditing/operation-logs/{validId:D}", readOnly))
        using (var response = await client.SendAsync(request, cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            Assert.IsFalse(payload.Contains("203.0.113.7", StringComparison.Ordinal));
            Assert.IsFalse(payload.Contains("secret-marker", StringComparison.Ordinal));
            Assert.IsFalse(payload.Contains("contextJson", StringComparison.OrdinalIgnoreCase));
        }

        foreach (var id in new[] { expiredId, invalidId, Guid.CreateVersion7() })
        {
            await AssertDetailsStatusAsync(client,
                $"/api/v1/auditing/operation-logs/{id:D}/details",
                both,
                HttpStatusCode.NotFound,
                cancellationToken);
        }
    }

    private static HttpRequestMessage AuthorizedGet(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task AssertDetailsStatusAsync(
        HttpClient client,
        string path,
        string token,
        HttpStatusCode expected,
        CancellationToken cancellationToken)
    {
        using var request = AuthorizedGet(path, token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(expected, response.StatusCode);
    }

    private static async Task VerifyListRequiresReadPermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/auditing/operation-logs?page=1&pageSize=20");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await factory.CreateHostAccessTokenAsync(
                ["platform.dashboard.read"],
                cancellationToken));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "authorization.permission_denied",
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyWriteAndQueryAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        await VerifyContainsTimeBoundaryAsync(
            client,
            adminToken,
            cancellationToken);
        var configKey = $"op.log.{Guid.NewGuid():N}"[..20];

        using var createRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/settings/config-entries")
        {
            Content = JsonContent.Create(new CreateConfigEntryRequest(
                configKey,
                "操作日志探针",
                null,
                null,
                ConfigValueKinds.String,
                "probe",
                1)),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);

        var referenceUtc = DateTimeOffset.UtcNow;
        var timeRangeQuery = CreateTimeRangeQuery(
            referenceUtc.AddHours(-12),
            referenceUtc.AddHours(1));
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/auditing/operation-logs?page=1&pageSize=50"
            + "&httpMethod=POST"
            + "&pathContains=/api/v1/settings/config-entries"
            + $"&{timeRangeQuery}");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content
            .ReadFromJsonAsync<PagedResult<OperationLogResponse>>(cancellationToken);
        Assert.IsNotNull(page);
        Assert.IsGreaterThan(0, page.Total);
        Assert.IsTrue(page.Items.Any(item =>
            item.RequestPath.Contains("/api/v1/settings/config-entries", StringComparison.Ordinal)
            && string.Equals(item.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase)
            && item.Succeeded));

        var first = page.Items[0];
        using var detailRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/auditing/operation-logs/{first.Id:D}");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var detailResponse = await client.SendAsync(detailRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, detailResponse.StatusCode);

        using var missingRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/auditing/operation-logs/{Guid.CreateVersion7():D}");
        missingRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var missingResponse = await client.SendAsync(missingRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await missingResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            AuditingErrorCodes.OperationLogNotFound,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyContainsTimeBoundaryAsync(
        HttpClient client,
        string adminToken,
        CancellationToken cancellationToken)
    {
        using var missingRangeRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/auditing/operation-logs?page=1&pageSize=1"
            + "&pathContains=%2Fapi");
        missingRangeRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var missingRangeResponse = await client.SendAsync(
            missingRangeRequest,
            cancellationToken);
        await AssertProblemAsync(
            missingRangeResponse,
            AuditingErrorCodes.ContainsTimeRangeRequired,
            cancellationToken);

        var referenceUtc = DateTimeOffset.UtcNow;
        var overLimitRange = CreateTimeRangeQuery(
            referenceUtc.AddDays(-2),
            referenceUtc);
        using var overLimitRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/auditing/operation-logs?page=1&pageSize=1"
            + $"&pathContains=%2Fapi&{overLimitRange}");
        overLimitRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var overLimitResponse = await client.SendAsync(
            overLimitRequest,
            cancellationToken);
        await AssertProblemAsync(
            overLimitResponse,
            AuditingErrorCodes.ContainsTimeRangeExceeded,
            cancellationToken);
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        string expectedCode,
        CancellationToken cancellationToken)
    {
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            expectedCode,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static string CreateTimeRangeQuery(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc) =>
        $"fromUtc={Uri.EscapeDataString($"{fromUtc:O}")}"
        + $"&toUtc={Uri.EscapeDataString($"{toUtc:O}")}";

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
