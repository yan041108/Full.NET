using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>Identity 机构单元投影运维闭环集成验收。</summary>
internal static class IdentityOrganizationUnitProjectionOperationsAssertions
{
    public static async Task VerifyBoundedReconciliationAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var hostAdminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var tenant = await EnterAcmeTenantAsync(client, hostAdminToken, cancellationToken);

        var firstCode = $"proj-first-{Guid.NewGuid():N}".ToLowerInvariant();
        var secondCode = $"proj-second-{Guid.NewGuid():N}".ToLowerInvariant();
        var firstUnit = await CreateUnitAsync(
            client,
            tenant.AccessToken,
            firstCode,
            "第一机构",
            cancellationToken);
        var secondUnit = await CreateUnitAsync(
            client,
            tenant.AccessToken,
            secondCode,
            "第二机构",
            cancellationToken);

        await VerifyPermissionGatesAsync(factory, client, tenant.TenantId, cancellationToken);

        var reconcileToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityOrganizationUnitProjectionPermissions.ReconcileDryRun,
                IdentityOrganizationUnitProjectionPermissions.ReconcileApply,
            ],
            cancellationToken);

        var firstDryRun = await ReconcileAsync(
            client,
            reconcileToken,
            tenant.TenantId,
            null,
            1,
            IdentityOrganizationUnitProjectionReconciliationModes.DryRun,
            cancellationToken);
        Assert.AreEqual(1, firstDryRun.Scanned);
        Assert.AreEqual(1, firstDryRun.Missing);
        Assert.AreEqual(0, firstDryRun.Applied);
        Assert.IsFalse(firstDryRun.IsComplete);
        Assert.IsNotNull(firstDryRun.NextAfterUnitId);

        var secondDryRun = await ReconcileAsync(
            client,
            reconcileToken,
            tenant.TenantId,
            firstDryRun.NextAfterUnitId,
            1,
            IdentityOrganizationUnitProjectionReconciliationModes.DryRun,
            cancellationToken);
        Assert.AreEqual(1, secondDryRun.Scanned);
        Assert.AreEqual(1, secondDryRun.Missing);
        Assert.IsTrue(secondDryRun.IsComplete);

        var firstApply = await ReconcileAsync(
            client,
            reconcileToken,
            tenant.TenantId,
            null,
            1,
            IdentityOrganizationUnitProjectionReconciliationModes.Apply,
            cancellationToken);
        Assert.AreEqual(1, firstApply.Applied);

        var secondApply = await ReconcileAsync(
            client,
            reconcileToken,
            tenant.TenantId,
            firstApply.NextAfterUnitId,
            1,
            IdentityOrganizationUnitProjectionReconciliationModes.Apply,
            cancellationToken);
        Assert.AreEqual(1, secondApply.Applied);

        var synced = await ReconcileAsync(
            client,
            reconcileToken,
            tenant.TenantId,
            null,
            100,
            IdentityOrganizationUnitProjectionReconciliationModes.Apply,
            cancellationToken);
        Assert.IsTrue(synced.Scanned >= 2);
        Assert.AreEqual(0, synced.Missing);
        Assert.AreEqual(0, synced.Stale);
        Assert.AreEqual(0, synced.Applied);

        using var renameRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/organization/units/{firstUnit.Id:D}",
            tenant.AccessToken,
            new UpdateOrganizationUnitRequest(
                firstUnit.ParentId?.ToString(),
                "重命名机构",
                firstUnit.DisplayOrder,
                firstUnit.Version));
        using var renameResponse = await client.SendAsync(renameRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, renameResponse.StatusCode);

        var staleDryRun = await ReconcileAsync(
            client,
            reconcileToken,
            tenant.TenantId,
            null,
            100,
            IdentityOrganizationUnitProjectionReconciliationModes.DryRun,
            cancellationToken);
        Assert.IsTrue(staleDryRun.Stale >= 1);
        Assert.AreEqual(0, staleDryRun.Applied);

        var staleApply = await ReconcileAsync(
            client,
            reconcileToken,
            tenant.TenantId,
            null,
            100,
            IdentityOrganizationUnitProjectionReconciliationModes.Apply,
            cancellationToken);
        Assert.IsTrue(staleApply.Applied >= 1);
    }

    private static async Task VerifyPermissionGatesAsync(
        FullNetApiFactory factory,
        HttpClient client,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var dryRunOnlyToken = await factory.CreateHostAccessTokenAsync(
            [IdentityOrganizationUnitProjectionPermissions.ReconcileDryRun],
            cancellationToken);
        var applyOnlyToken = await factory.CreateHostAccessTokenAsync(
            [IdentityOrganizationUnitProjectionPermissions.ReconcileApply],
            cancellationToken);

        using var forbiddenApplyRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/organization-unit-projections/reconcile",
            dryRunOnlyToken,
            new ReconcileOrganizationUnitProjectionRequest(
                tenantId,
                null,
                10,
                IdentityOrganizationUnitProjectionReconciliationModes.Apply));
        using var forbiddenApplyResponse = await client.SendAsync(
            forbiddenApplyRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenApplyResponse.StatusCode);

        using var forbiddenDryRunRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/organization-unit-projections/reconcile",
            applyOnlyToken,
            new ReconcileOrganizationUnitProjectionRequest(
                tenantId,
                null,
                10,
                IdentityOrganizationUnitProjectionReconciliationModes.DryRun));
        using var forbiddenDryRunResponse = await client.SendAsync(
            forbiddenDryRunRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenDryRunResponse.StatusCode);
    }

    private static async Task<ReconcileOrganizationUnitProjectionResponse> ReconcileAsync(
        HttpClient client,
        string accessToken,
        Guid tenantId,
        Guid? afterUnitId,
        int pageSize,
        string mode,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/organization-unit-projections/reconcile",
            accessToken,
            new ReconcileOrganizationUnitProjectionRequest(
                tenantId,
                afterUnitId,
                pageSize,
                mode));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content
            .ReadFromJsonAsync<ReconcileOrganizationUnitProjectionResponse>(cancellationToken);
        Assert.IsNotNull(payload);
        return payload;
    }

    private static async Task<OrganizationUnitResponse> CreateUnitAsync(
        HttpClient client,
        string tenantToken,
        string code,
        string name,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/units",
            tenantToken,
            new CreateOrganizationUnitRequest(null, code, name, 10));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<OrganizationUnitResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        return created;
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
        var loginToken = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(loginToken);
        return loginToken.AccessToken;
    }

    private static async Task<TenantSession> EnterAcmeTenantAsync(
        HttpClient client,
        string hostAccessToken,
        CancellationToken cancellationToken)
    {
        using var availableRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/available");
        availableRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            hostAccessToken);
        using var availableResponse = await client.SendAsync(availableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, availableResponse.StatusCode);
        var available = await availableResponse.Content
            .ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken);
        Assert.IsNotNull(available);
        var acme = available.Single(tenant => tenant.Identifier == "acme");

        using var enterRequest = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/tenancy/context")
        {
            Content = JsonContent.Create(new ChangeTenantContextRequest(acme.Id)),
        };
        enterRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            hostAccessToken);
        using var enterResponse = await client.SendAsync(enterRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, enterResponse.StatusCode);
        var entered = await enterResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(entered);
        return new TenantSession(acme.Id, entered.AccessToken);
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

    private sealed record TenantSession(Guid TenantId, string AccessToken);
}