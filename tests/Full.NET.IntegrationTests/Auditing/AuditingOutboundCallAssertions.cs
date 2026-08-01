using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Auditing.Features.TriggerOutboundCallProbe;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Auditing;

/// <summary>Host 出站调用审计纵向切片验收夹具。</summary>
internal static class AuditingOutboundCallAssertions
{
    private const string SensitiveMarker = "Bearer fnk_super_secret_cookie=session";

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        await VerifyWriteAndQueryAsync(client, cancellationToken);
        await VerifySensitivePayloadIsNotPersistedAsync(factory, client, cancellationToken);
        await OpenApiAuditingOutboundCallLogsContractAssertions.VerifyAsync(
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
            "/api/v1/auditing/outbound-call-logs?page=1&pageSize=20");
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
        await VerifyContainsTimeBoundaryAsync(client, adminToken, cancellationToken);
        var traceId = $"outbound-{Guid.NewGuid():N}";

        using var probeRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auditing/outbound-call-probes")
        {
            Content = JsonContent.Create(
                new OutboundCallAuditProbeRequest(
                    new OutboundCallAuditRequest(
                        Endpoint.ProbeProviderKey,
                        Endpoint.ProbeOperationKey,
                        "api.probe.example.com",
                        502,
                        false,
                        88,
                        1,
                        traceId,
                        "payments.upstream.unavailable"),
                    SensitiveMarker)),
        };
        probeRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var probeResponse = await client.SendAsync(probeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NoContent, probeResponse.StatusCode);

        var referenceUtc = DateTimeOffset.UtcNow;
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/auditing/outbound-call-logs?page=1&pageSize=20"
            + "&providerKey=auditing.outbound_probe"
            + "&succeeded=false"
            + $"&fromUtc={Uri.EscapeDataString(referenceUtc.AddHours(-1).ToString("O"))}"
            + $"&toUtc={Uri.EscapeDataString(referenceUtc.AddHours(1).ToString("O"))}");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content
            .ReadFromJsonAsync<PagedResult<OutboundCallLogResponse>>(cancellationToken);
        Assert.IsNotNull(page);
        Assert.IsGreaterThan(0, page.Total);
        var first = page.Items.Single(item => item.TraceId == traceId);
        Assert.AreEqual("api.probe.example.com", first.DestinationHostCategory);
        Assert.AreEqual("payments.upstream.unavailable", first.SafeErrorCode);

        using var detailRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/auditing/outbound-call-logs/{first.Id:D}");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var detailResponse = await client.SendAsync(detailRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, detailResponse.StatusCode);

        using var missingRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/auditing/outbound-call-logs/{Guid.CreateVersion7():D}");
        missingRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var missingResponse = await client.SendAsync(missingRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await missingResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            AuditingErrorCodes.OutboundCallLogNotFound,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifySensitivePayloadIsNotPersistedAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var traceId = $"redact-{Guid.NewGuid():N}";
        using var probeRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auditing/outbound-call-probes")
        {
            Content = JsonContent.Create(
                new OutboundCallAuditProbeRequest(
                    new OutboundCallAuditRequest(
                        $"api_key={SensitiveMarker}",
                        "password=secret",
                        $"https://evil.example/path?token={SensitiveMarker}",
                        401,
                        false,
                        10,
                        0,
                        traceId,
                        $"Authorization: {SensitiveMarker}"),
                    SensitiveMarker)),
        };
        probeRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var probeResponse = await client.SendAsync(probeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NoContent, probeResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<Full.NET.Abstractions.Tenancy.CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            var row = await scope.ServiceProvider
                .GetRequiredService<Full.NET.Data.Abstractions.IQueryExecutor>()
                .QuerySingleOrDefaultAsync<OutboundAuditRow>(
                    new Full.NET.Data.Abstractions.SqlStatement(
                        "integration.auditing.read_outbound_row",
                        """
                        SELECT ProviderKey, OperationKey, DestinationHostCategory, SafeErrorCode
                        FROM fn_auditing_outbound_call
                        WHERE TraceId = @TraceId
                        """,
                        Full.NET.Data.Abstractions.SqlDataScope.HostOnly),
                    new { TraceId = traceId },
                    cancellationToken);
            Assert.IsNotNull(row);
            var serialized = string.Join(
                '|',
                row.ProviderKey,
                row.OperationKey,
                row.DestinationHostCategory,
                row.SafeErrorCode ?? string.Empty);
            Assert.IsFalse(serialized.Contains(SensitiveMarker, StringComparison.Ordinal));
            Assert.IsFalse(serialized.Contains("password=", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(serialized.Contains("api_key=", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(serialized.Contains("Authorization:", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(serialized.Contains("?token=", StringComparison.Ordinal));
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private static async Task VerifyContainsTimeBoundaryAsync(
        HttpClient client,
        string adminToken,
        CancellationToken cancellationToken)
    {
        using var missingRangeRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/auditing/outbound-call-logs?page=1&pageSize=1"
            + "&operationContains=probe");
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
            "/api/v1/auditing/outbound-call-logs?page=1&pageSize=1"
            + $"&operationContains=probe&{overLimitRange}");
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
            Content = JsonContent.Create(new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        Assert.IsNotNull(token);
        return token.AccessToken;
    }

    private sealed class OutboundAuditRow
    {
        public string ProviderKey { get; init; } = string.Empty;

        public string OperationKey { get; init; } = string.Empty;

        public string DestinationHostCategory { get; init; } = string.Empty;

        public string? SafeErrorCode { get; init; }
    }
}
