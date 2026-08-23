using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Settings.Contracts;

namespace Full.NET.IntegrationTests.Settings;

/// <summary>
/// Host 限时诊断策略纵向切片：权限、TTL/作用域硬上限、恢复默认与 B0 同事务审计。
/// </summary>
internal static class DiagnosticPolicyAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await OpenApiSettingsDiagnosticPolicyContractAssertions.VerifyAsync(client, cancellationToken);
        await VerifyReadRequiresPermissionAsync(factory, client, cancellationToken);
        await VerifyUpdateRejectsInvalidScopeAndTtlAsync(client, cancellationToken);
        await VerifyUpdateRestoreAndDomainAuditAsync(factory, client, cancellationToken);
        await VerifyExactDiagnosticPolicyActionPermissionBoundariesAsync(
            factory,
            client,
            cancellationToken);
    }

    private static async Task VerifyReadRequiresPermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/settings/diagnostic-policy");
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

    private static async Task VerifyUpdateRejectsInvalidScopeAndTtlAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        using var getRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/settings/diagnostic-policy");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var getResponse = await client.SendAsync(getRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);
        var current = await getResponse.Content.ReadFromJsonAsync<DiagnosticPolicyResponse>(
            cancellationToken);
        Assert.IsNotNull(current);

        using var invalidScope = CreateBearerJsonRequest(
            HttpMethod.Put,
            "/api/v1/settings/diagnostic-policy",
            adminToken,
            new UpdateDiagnosticPolicyRequest(
                "Normal",
                [
                    new DiagnosticPolicyRuleRequest(
                        "Sink",
                        "elasticsearch",
                        1.0,
                        null,
                        null,
                        null,
                        DateTimeOffset.UtcNow.AddMinutes(30)),
                ],
                current.ConfigEntryVersion));
        using var invalidScopeResponse = await client.SendAsync(invalidScope, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidScopeResponse.StatusCode);

        using var invalidTtl = CreateBearerJsonRequest(
            HttpMethod.Put,
            "/api/v1/settings/diagnostic-policy",
            adminToken,
            new UpdateDiagnosticPolicyRequest(
                "Normal",
                [
                    new DiagnosticPolicyRuleRequest(
                        "Endpoint",
                        "orders/{id}",
                        1.0,
                        null,
                        null,
                        null,
                        DateTimeOffset.UtcNow.AddSeconds(10)),
                ],
                current.ConfigEntryVersion));
        using var invalidTtlResponse = await client.SendAsync(invalidTtl, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidTtlResponse.StatusCode);
    }

    private static async Task VerifyUpdateRestoreAndDomainAuditAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        using var getRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/settings/diagnostic-policy");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var getResponse = await client.SendAsync(getRequest, cancellationToken);
        var current = await getResponse.Content.ReadFromJsonAsync<DiagnosticPolicyResponse>(
            cancellationToken);
        Assert.IsNotNull(current);

        var expires = DateTimeOffset.UtcNow.AddMinutes(30);
        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            "/api/v1/settings/diagnostic-policy",
            adminToken,
            new UpdateDiagnosticPolicyRequest(
                "Degraded",
                [
                    new DiagnosticPolicyRuleRequest(
                        "Endpoint",
                        "demo/{id}",
                        1.0,
                        50,
                        1024,
                        2048,
                        expires),
                ],
                current.ConfigEntryVersion));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<DiagnosticPolicyResponse>(
            cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual("Degraded", updated.PressureState);
        Assert.IsFalse(updated.IsDefault);
        Assert.HasCount(1, updated.ActiveRules);

        using var rereadRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/settings/diagnostic-policy");
        rereadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var rereadResponse = await client.SendAsync(rereadRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, rereadResponse.StatusCode);
        var reread = await rereadResponse.Content.ReadFromJsonAsync<DiagnosticPolicyResponse>(
            cancellationToken);
        Assert.IsNotNull(reread);
        Assert.AreEqual("Degraded", reread.PressureState);
        Assert.IsFalse(reread.IsDefault);

        Assert.IsGreaterThan(
            0,
            await CountDomainAuditRowsAsync(factory, cancellationToken));

        using var restoreRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/settings/diagnostic-policy/restore",
            adminToken,
            new RestoreDiagnosticPolicyRequest(updated.ConfigEntryVersion));
        using var restoreResponse = await client.SendAsync(restoreRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, restoreResponse.StatusCode);
        var restored = await restoreResponse.Content.ReadFromJsonAsync<DiagnosticPolicyResponse>(
            cancellationToken);
        Assert.IsNotNull(restored);
        Assert.IsTrue(restored.IsDefault);
        Assert.AreEqual("Normal", restored.PressureState);
        Assert.IsEmpty(restored.ActiveRules);
    }

    private static async Task VerifyExactDiagnosticPolicyActionPermissionBoundariesAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        using var getRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/settings/diagnostic-policy");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var getResponse = await client.SendAsync(getRequest, cancellationToken);
        var current = await getResponse.Content.ReadFromJsonAsync<DiagnosticPolicyResponse>(
            cancellationToken);
        Assert.IsNotNull(current);

        var readOnlyToken = await factory.CreateHostAccessTokenAsync(
            [DiagnosticPolicyManagementPermissions.Read],
            cancellationToken);
        await AssertDiagnosticPolicyPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Put,
            "/api/v1/settings/diagnostic-policy",
            cancellationToken,
            new UpdateDiagnosticPolicyRequest(
                "Normal",
                [],
                current.ConfigEntryVersion));
        await AssertDiagnosticPolicyPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            "/api/v1/settings/diagnostic-policy/restore",
            cancellationToken,
            new RestoreDiagnosticPolicyRequest(current.ConfigEntryVersion));

        var updateToken = await factory.CreateHostAccessTokenAsync(
            [
                DiagnosticPolicyManagementPermissions.Read,
                DiagnosticPolicyManagementPermissions.Update,
            ],
            cancellationToken);
        await AssertDiagnosticPolicyPermissionDeniedAsync(
            client,
            updateToken,
            HttpMethod.Post,
            "/api/v1/settings/diagnostic-policy/restore",
            cancellationToken,
            new RestoreDiagnosticPolicyRequest(current.ConfigEntryVersion));

        var restoreToken = await factory.CreateHostAccessTokenAsync(
            [
                DiagnosticPolicyManagementPermissions.Read,
                DiagnosticPolicyManagementPermissions.Restore,
            ],
            cancellationToken);
        using var restoreRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/settings/diagnostic-policy/restore",
            restoreToken,
            new RestoreDiagnosticPolicyRequest(current.ConfigEntryVersion));
        using var restoreResponse = await client.SendAsync(restoreRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, restoreResponse.StatusCode);
    }

    private static async Task AssertDiagnosticPolicyPermissionDeniedAsync<TRequest>(
        HttpClient client,
        string accessToken,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken,
        TRequest? body)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            CommonErrorCodes.PermissionDenied,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task<long> CountDomainAuditRowsAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            return await scope.ServiceProvider
                .GetRequiredService<IQueryExecutor>()
                .QuerySingleOrDefaultAsync<long>(
                    new SqlStatement(
                        "test.settings.count_diagnostic_policy_domain_audit",
                        """
                        SELECT COUNT(1)
                        FROM fn_settings_domain_audit
                        WHERE ActionKey = 'settings.logging-diagnostic-policy.updated'
                        """,
                        SqlDataScope.HostOnly),
                    parameters: null,
                    cancellationToken: cancellationToken);
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

    private static HttpRequestMessage CreateBearerJsonRequest<TRequest>(
        HttpMethod method,
        string path,
        string accessToken,
        TRequest body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
    }
}