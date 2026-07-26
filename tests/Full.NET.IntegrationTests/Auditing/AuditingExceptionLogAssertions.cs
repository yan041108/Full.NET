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

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/auditing/exception-logs?page=1&pageSize=50&pathContains=/api/v1/auditing/exception-probes");
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
