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
/// Host 异常日志纵向切片验收夹具。
/// </summary>
internal static class AuditingExceptionLogAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        await VerifyProbeWriteAndQueryAsync(factory, client, cancellationToken);
        await OpenApiAuditingExceptionLogsContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifyListRequiresReadPermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/auditing/exception-logs?page=1&pageSize=20");
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

    private static async Task VerifyProbeWriteAndQueryAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        await VerifyContainsTimeBoundaryAsync(
            client,
            adminToken,
            cancellationToken);

        using var probeRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auditing/exception-probes");
        probeRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var probeResponse = await client.SendAsync(probeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.InternalServerError, probeResponse.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(
                new SqlStatement(
                    "test.restore_legacy_exception_log_payload",
                    """
                    UPDATE fn_auditing_exception_log
                    SET Message = @Message,
                        StackTrace = @StackTrace
                    WHERE RequestPath = @RequestPath
                    """,
                    SqlDataScope.HostOnly),
                new
                {
                    Message = "database-password=legacy-secret",
                    StackTrace = "at SensitiveProbe(database-password=legacy-secret)",
                    RequestPath = "/api/v1/auditing/exception-probes",
                },
                cancellationToken);
        }

        var referenceUtc = DateTimeOffset.UtcNow;
        var timeRangeQuery = CreateTimeRangeQuery(
            referenceUtc.AddHours(-12),
            referenceUtc.AddHours(1));
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/auditing/exception-logs?page=1&pageSize=50"
            + "&pathContains=/api/v1/auditing/exception-probes"
            + $"&{timeRangeQuery}");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content
            .ReadFromJsonAsync<PagedResult<ExceptionLogResponse>>(cancellationToken);
        Assert.IsNotNull(page);
        Assert.IsGreaterThan(0, page.Total);
        Assert.IsTrue(page.Items.All(item =>
            string.Equals(
                item.Message,
                "Unhandled application exception.",
                StringComparison.Ordinal)
            && item.StackTrace is null));

        var first = page.Items[0];
        using var detailRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/auditing/exception-logs/{first.Id:D}");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var detailResponse = await client.SendAsync(detailRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content
            .ReadFromJsonAsync<ExceptionLogResponse>(cancellationToken);
        Assert.IsNotNull(detail);
        Assert.AreEqual("Unhandled application exception.", detail.Message);
        Assert.IsNull(detail.StackTrace);

        using var missingRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/auditing/exception-logs/{Guid.CreateVersion7():D}");
        missingRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var missingResponse = await client.SendAsync(missingRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await missingResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            AuditingErrorCodes.ExceptionLogNotFound,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyContainsTimeBoundaryAsync(
        HttpClient client,
        string adminToken,
        CancellationToken cancellationToken)
    {
        using var missingRangeRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/auditing/exception-logs?page=1&pageSize=1"
            + "&exceptionTypeContains=InvalidOperationException");
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
            "/api/v1/auditing/exception-logs?page=1&pageSize=1"
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
