using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Organization;

/// <summary>
/// 租户用户-机构隶属纵向切片验收夹具。
/// </summary>
internal static class OrganizationUserUnitManagementAssertions
{
    public static async Task VerifyTenantUserUnitManagementContractAsync(
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
        await OpenApiOrganizationTenantUserUnitsContractAssertions.VerifyAsync(
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
                    "tenancy.tenants.switch",
                ],
                cancellationToken),
            cancellationToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/organization/user-units?page=1&pageSize=20");
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
        var (userId, unitId) = await CreateFixtureUnitAndResolveAdminUserAsync(
            client,
            adminTenantToken,
            cancellationToken);
        var body = new CreateOrganizationUserUnitRequest(userId, unitId, true);

        using var firstRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/user-units",
            adminTenantToken,
            body);
        using var firstResponse = await client.SendAsync(firstRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);

        using var duplicateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/user-units",
            adminTenantToken,
            body);
        using var duplicateResponse = await client.SendAsync(
            duplicateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await duplicateResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            OrganizationErrorCodes.UserUnitAlreadyAssigned,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyCustomAssignmentLifecycleAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminTenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var (userId, unitId) = await CreateFixtureUnitAndResolveAdminUserAsync(
            client,
            adminTenantToken,
            cancellationToken);

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/user-units",
            adminTenantToken,
            new CreateOrganizationUserUnitRequest(userId, unitId, false));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content
            .ReadFromJsonAsync<OrganizationUserUnitResponse>(cancellationToken);
        Assert.IsNotNull(created);
        Assert.IsFalse(created.IsPrimary);

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/organization/user-units/{created.Id:D}",
            adminTenantToken,
            new UpdateOrganizationUserUnitRequest(true, created.Version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content
            .ReadFromJsonAsync<OrganizationUserUnitResponse>(cancellationToken);
        Assert.IsNotNull(updated);
        Assert.IsTrue(updated.IsPrimary);

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/organization/user-units/{created.Id:D}/disable",
            adminTenantToken,
            new { });
        using var disableResponse = await client.SendAsync(
            disableRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
        var disabled = await disableResponse.Content
            .ReadFromJsonAsync<OrganizationUserUnitResponse>(cancellationToken);
        Assert.IsNotNull(disabled);
        Assert.IsFalse(disabled.IsActive);
        Assert.IsFalse(disabled.IsPrimary);
    }

    private static async Task<(Guid UserId, Guid UnitId)> CreateFixtureUnitAndResolveAdminUserAsync(
        HttpClient client,
        string adminTenantToken,
        CancellationToken cancellationToken)
    {
        using var usersRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=20");
        usersRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminTenantToken);
        using var usersResponse = await client.SendAsync(usersRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, usersResponse.StatusCode);
        var usersPage = await usersResponse.Content
            .ReadFromJsonAsync<PagedResult<HostUserResponse>>(cancellationToken);
        Assert.IsNotNull(usersPage);
        var admin = usersPage.Items.Single(user => user.Username == "admin");

        var code = $"unit-{Guid.NewGuid():N}".ToLowerInvariant();
        using var createUnitRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/units",
            adminTenantToken,
            new CreateOrganizationUnitRequest(null, code, "隶属测试机构", 10));
        using var createUnitResponse = await client.SendAsync(
            createUnitRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createUnitResponse.StatusCode);
        var unit = await createUnitResponse.Content.ReadFromJsonAsync<OrganizationUnitResponse>(
            cancellationToken);
        Assert.IsNotNull(unit);
        return (admin.Id, unit.Id);
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
