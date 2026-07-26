using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Organization;

/// <summary>
/// 租户用户-职位隶属纵向切片验收夹具。
/// </summary>
internal static class OrganizationUserPositionManagementAssertions
{
    public static async Task VerifyTenantUserPositionManagementContractAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionInTenantContextAsync(
            factory,
            client,
            cancellationToken);
        await VerifyCreateRejectsDuplicateAssignmentAsync(client, cancellationToken);
        await VerifyCustomAssignmentLifecycleAsync(client, cancellationToken);
        await OpenApiOrganizationTenantUserPositionsContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifyListRequiresReadPermissionInTenantContextAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var tenantToken = await EnterAcmeTenantAsync(
            client,
            await factory.CreateHostAccessTokenAsync(
                [
                    "platform.dashboard.read",
                    "tenancy.tenants.read",
                    "tenancy.tenants.switch",
                ],
                cancellationToken),
            cancellationToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/organization/user-positions?page=1&pageSize=20");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            tenantToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "authorization.permission_denied",
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyCreateRejectsDuplicateAssignmentAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminTenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var (userId, positionId) = await CreateFixturePositionAndResolveAdminUserAsync(
            client,
            adminTenantToken,
            cancellationToken);
        var body = new CreateOrganizationUserPositionRequest(userId, positionId, true);

        using var firstRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/user-positions",
            adminTenantToken,
            body);
        using var firstResponse = await client.SendAsync(firstRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);

        using var duplicateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/user-positions",
            adminTenantToken,
            body);
        using var duplicateResponse = await client.SendAsync(
            duplicateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await duplicateResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            OrganizationErrorCodes.UserPositionAlreadyAssigned,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyCustomAssignmentLifecycleAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminTenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var (userId, positionId) = await CreateFixturePositionAndResolveAdminUserAsync(
            client,
            adminTenantToken,
            cancellationToken);

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/user-positions",
            adminTenantToken,
            new CreateOrganizationUserPositionRequest(userId, positionId, false));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content
            .ReadFromJsonAsync<OrganizationUserPositionResponse>(cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual("admin", created.Username);
        Assert.AreEqual("系统管理员", created.DisplayName);
        Assert.IsFalse(created.IsPrimary);

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/organization/user-positions/{created.Id:D}",
            adminTenantToken,
            new UpdateOrganizationUserPositionRequest(true, created.Version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content
            .ReadFromJsonAsync<OrganizationUserPositionResponse>(cancellationToken);
        Assert.IsNotNull(updated);
        Assert.IsTrue(updated.IsPrimary);

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/organization/user-positions/{created.Id:D}/disable",
            adminTenantToken,
            new { });
        using var disableResponse = await client.SendAsync(
            disableRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
        var disabled = await disableResponse.Content
            .ReadFromJsonAsync<OrganizationUserPositionResponse>(cancellationToken);
        Assert.IsNotNull(disabled);
        Assert.IsFalse(disabled.IsActive);
        Assert.IsFalse(disabled.IsPrimary);
    }

    private static async Task<(Guid UserId, Guid PositionId)> CreateFixturePositionAndResolveAdminUserAsync(
        HttpClient client,
        string adminTenantToken,
        CancellationToken cancellationToken)
    {
        using var currentUserRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/me");
        currentUserRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminTenantToken);
        using var currentUserResponse = await client.SendAsync(
            currentUserRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, currentUserResponse.StatusCode);
        var admin = await currentUserResponse.Content
            .ReadFromJsonAsync<CurrentUserResponse>(cancellationToken);
        Assert.IsNotNull(admin);
        Assert.AreEqual("admin", admin.Username);

        var code = $"pos-{Guid.NewGuid():N}"[..20].ToLowerInvariant();
        using var createPositionRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/positions",
            adminTenantToken,
            new CreateOrganizationPositionRequest(code, "隶属测试职位", 10));
        using var createPositionResponse = await client.SendAsync(
            createPositionRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createPositionResponse.StatusCode);
        var position = await createPositionResponse.Content
            .ReadFromJsonAsync<OrganizationPositionResponse>(cancellationToken);
        Assert.IsNotNull(position);
        return (admin.Id, position.Id);
    }

    private static async Task<string> LoginAndEnterAcmeTenantAsync(
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
        return await EnterAcmeTenantAsync(client, loginToken.AccessToken, cancellationToken);
    }

    private static async Task<string> EnterAcmeTenantAsync(
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
        return entered.AccessToken;
    }

    private static HttpRequestMessage CreateBearerJsonRequest<TRequest>(
        HttpMethod method,
        string path,
        string accessToken,
        TRequest body)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null && method != HttpMethod.Get)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
    }
}
