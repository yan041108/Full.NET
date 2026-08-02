using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Tenancy;

/// <summary>
/// Host 租户套餐目录纵向切片验收夹具。
/// </summary>
internal static class TenancyHostTenantPackageManagementAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresPackageReadPermissionAsync(factory, client, cancellationToken);
        await VerifyCreateRejectsDuplicateCodeAsync(client, cancellationToken);
        await VerifyUpdateWithOptimisticVersionAsync(client, cancellationToken);
        await VerifyDisableSetsPackageInactiveAsync(client, cancellationToken);
        await VerifyDisableRejectsAssignedPackageAsync(client, cancellationToken);
        await VerifyListIncludesAssignedTenantCountAsync(client, cancellationToken);
        await VerifyExactTenantPackageActionPermissionBoundariesAsync(
            factory,
            client,
            cancellationToken);
        await Api.OpenApiHostTenantPackagesContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifyListRequiresPackageReadPermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/tenant-packages?page=1&pageSize=20");
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

    private static async Task VerifyCreateRejectsDuplicateCodeAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var code = $"pkg-{Guid.NewGuid():N}"[..12];
        var body = new CreateHostTenantPackageRequest(code, "集成测试套餐", "描述");

        using var firstRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/tenancy/tenant-packages",
            adminToken,
            body);
        using var firstResponse = await client.SendAsync(firstRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);
        var created = await firstResponse.Content.ReadFromJsonAsync<TenantPackageSummary>(
            cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(code, created.Code);

        using var duplicateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/tenancy/tenant-packages",
            adminToken,
            body);
        using var duplicateResponse = await client.SendAsync(
            duplicateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await duplicateResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            TenancyErrorCodes.PackageCodeExists,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyUpdateWithOptimisticVersionAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var code = $"upd-{Guid.NewGuid():N}"[..12];

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/tenancy/tenant-packages",
            adminToken,
            new CreateHostTenantPackageRequest(code, "更新前名称", null));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TenantPackageSummary>(
            cancellationToken);
        Assert.IsNotNull(created);

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/tenancy/tenant-packages/{created.Id:D}",
            adminToken,
            new UpdateHostTenantPackageRequest("更新后名称", "新描述", created.Version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<TenantPackageSummary>(
            cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual("更新后名称", updated.Name);
        Assert.AreEqual("新描述", updated.Description);
        Assert.AreEqual(created.Version + 1, updated.Version);

        using var staleRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/tenancy/tenant-packages/{created.Id:D}",
            adminToken,
            new UpdateHostTenantPackageRequest("陈旧版本", null, created.Version));
        using var staleResponse = await client.SendAsync(staleRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, staleResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await staleResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            TenancyErrorCodes.PackageVersionConflict,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyDisableSetsPackageInactiveAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var code = $"dis-{Guid.NewGuid():N}"[..12];

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/tenancy/tenant-packages",
            adminToken,
            new CreateHostTenantPackageRequest(code, "待禁用套餐", null));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TenantPackageSummary>(
            cancellationToken);
        Assert.IsNotNull(created);
        Assert.IsTrue(created.IsActive);

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenant-packages/{created.Id:D}/disable",
            adminToken,
            new { });
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
        var disabled = await disableResponse.Content.ReadFromJsonAsync<TenantPackageSummary>(
            cancellationToken);
        Assert.IsNotNull(disabled);
        Assert.IsFalse(disabled.IsActive);
        Assert.AreEqual(created.Version + 1, disabled.Version);
    }

    private static async Task VerifyDisableRejectsAssignedPackageAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var code = $"use-{Guid.NewGuid():N}"[..12];

        using var createPackageRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/tenancy/tenant-packages",
            adminToken,
            new CreateHostTenantPackageRequest(code, "绑定中套餐", null));
        using var createPackageResponse = await client.SendAsync(
            createPackageRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createPackageResponse.StatusCode);
        var package = await createPackageResponse.Content.ReadFromJsonAsync<TenantPackageSummary>(
            cancellationToken);
        Assert.IsNotNull(package);

        var identifier = $"pkg-{Guid.NewGuid():N}"[..14];
        using var provisionRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/tenancy/tenants",
            adminToken,
            new ProvisionTenantRequest(
                identifier,
                "绑定套餐租户",
                $"{identifier}.localhost",
                package.Id));
        using var provisionResponse = await client.SendAsync(provisionRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, provisionResponse.StatusCode);
        var tenant = await provisionResponse.Content.ReadFromJsonAsync<TenantSummary>(cancellationToken);
        Assert.IsNotNull(tenant);

        using var disableWhileAssignedRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenant-packages/{package.Id:D}/disable",
            adminToken,
            new { });
        using var disableWhileAssignedResponse = await client.SendAsync(
            disableWhileAssignedRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, disableWhileAssignedResponse.StatusCode);
        using var inUseProblem = JsonDocument.Parse(
            await disableWhileAssignedResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            TenancyErrorCodes.PackageInUse,
            inUseProblem.RootElement.GetProperty("code").GetString());

        using var unassignRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/package",
            adminToken,
            new AssignHostTenantPackageRequest(null, tenant.Version));
        using var unassignResponse = await client.SendAsync(unassignRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, unassignResponse.StatusCode);
        var unassigned = await unassignResponse.Content.ReadFromJsonAsync<TenantSummary>(cancellationToken);
        Assert.IsNotNull(unassigned);
        Assert.IsNull(unassigned.TenantPackageId);

        using var disableAfterUnassignRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenant-packages/{package.Id:D}/disable",
            adminToken,
            new { });
        using var disableAfterUnassignResponse = await client.SendAsync(
            disableAfterUnassignRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableAfterUnassignResponse.StatusCode);
        var disabled = await disableAfterUnassignResponse.Content.ReadFromJsonAsync<TenantPackageSummary>(
            cancellationToken);
        Assert.IsNotNull(disabled);
        Assert.IsFalse(disabled.IsActive);
    }

    private static async Task VerifyListIncludesAssignedTenantCountAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var code = $"cnt-{Guid.NewGuid():N}"[..12];

        using var createPackageRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/tenancy/tenant-packages",
            adminToken,
            new CreateHostTenantPackageRequest(code, "计数套餐", null));
        using var createPackageResponse = await client.SendAsync(
            createPackageRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createPackageResponse.StatusCode);
        var package = await createPackageResponse.Content.ReadFromJsonAsync<TenantPackageSummary>(
            cancellationToken);
        Assert.IsNotNull(package);
        Assert.AreEqual(0, package.AssignedTenantCount);

        var identifier = $"cnt-{Guid.NewGuid():N}"[..14];
        using var provisionRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/tenancy/tenants",
            adminToken,
            new ProvisionTenantRequest(
                identifier,
                "计数租户",
                $"{identifier}.localhost",
                package.Id));
        using var provisionResponse = await client.SendAsync(provisionRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, provisionResponse.StatusCode);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/tenant-packages?page=1&pageSize=100");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedTenantPackageSummaries>(
            cancellationToken);
        Assert.IsNotNull(page);
        var listed = page.Items.Single(item => item.Id == package.Id);
        Assert.AreEqual(1, listed.AssignedTenantCount);

        using var detailRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/tenancy/tenant-packages/{package.Id:D}");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var detailResponse = await client.SendAsync(detailRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<TenantPackageSummary>(
            cancellationToken);
        Assert.IsNotNull(detail);
        Assert.AreEqual(1, detail.AssignedTenantCount);
    }

    private static async Task VerifyExactTenantPackageActionPermissionBoundariesAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var code = $"bound-{Guid.NewGuid():N}"[..14];
        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/tenancy/tenant-packages",
            adminToken,
            new CreateHostTenantPackageRequest(code, "边界测试套餐", null));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TenantPackageSummary>(
            cancellationToken);
        Assert.IsNotNull(created);

        var disableCode = $"dis-{Guid.NewGuid():N}"[..14];
        using var disableTargetRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/tenancy/tenant-packages",
            adminToken,
            new CreateHostTenantPackageRequest(disableCode, "禁用边界套餐", null));
        using var disableTargetResponse = await client.SendAsync(
            disableTargetRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, disableTargetResponse.StatusCode);
        var disableTarget = await disableTargetResponse.Content.ReadFromJsonAsync<TenantPackageSummary>(
            cancellationToken);
        Assert.IsNotNull(disableTarget);

        var readOnlyToken = await factory.CreateHostAccessTokenAsync(
            [TenancyTenantPackagePermissions.Read],
            cancellationToken);
        await AssertTenantPackagePermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            "/api/v1/tenancy/tenant-packages",
            cancellationToken,
            new CreateHostTenantPackageRequest("denied", "拒绝", null));
        await AssertTenantPackagePermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Put,
            $"/api/v1/tenancy/tenant-packages/{created.Id:D}",
            cancellationToken,
            new UpdateHostTenantPackageRequest("拒绝更新", null, created.Version));
        await AssertTenantPackagePermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            $"/api/v1/tenancy/tenant-packages/{created.Id:D}/disable",
            cancellationToken);

        var createToken = await factory.CreateHostAccessTokenAsync(
            [
                TenancyTenantPackagePermissions.Read,
                TenancyTenantPackagePermissions.Create,
            ],
            cancellationToken);
        await AssertTenantPackagePermissionDeniedAsync(
            client,
            createToken,
            HttpMethod.Put,
            $"/api/v1/tenancy/tenant-packages/{created.Id:D}",
            cancellationToken,
            new UpdateHostTenantPackageRequest("拒绝更新", null, created.Version));
        await AssertTenantPackagePermissionDeniedAsync(
            client,
            createToken,
            HttpMethod.Post,
            $"/api/v1/tenancy/tenant-packages/{created.Id:D}/disable",
            cancellationToken);

        var disableToken = await factory.CreateHostAccessTokenAsync(
            [
                TenancyTenantPackagePermissions.Read,
                TenancyTenantPackagePermissions.Disable,
            ],
            cancellationToken);
        using var disableRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenant-packages/{disableTarget.Id:D}/disable");
        disableRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            disableToken);
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
    }

    private static async Task AssertTenantPackagePermissionDeniedAsync(
        HttpClient client,
        string accessToken,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken,
        object? body = null)
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

    private sealed record PagedTenantPackageSummaries(
        TenantPackageSummary[] Items,
        int Page,
        int PageSize,
        long Total);

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
