using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>
/// Host 菜单管理纵向切片验收夹具。
/// </summary>
internal static class IdentityMenuManagementAssertions
{
    public static async Task VerifyHostMenuManagementContractAsync(
        Api.FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        await VerifyCreateRejectsDuplicateRouteNameAsync(client, cancellationToken);
        await VerifyCustomMenuLifecycleAndNavigationProjectionAsync(client, cancellationToken);
        await OpenApiHostMenusContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifyListRequiresReadPermissionAsync(
        Api.FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/menus?page=1&pageSize=20");
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

    private static async Task VerifyCreateRejectsDuplicateRouteNameAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var routeName = $"menu-{Guid.NewGuid():N}".ToLowerInvariant();
        var body = CreateSampleMenuRequest(routeName);

        using var firstRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/menus",
            adminToken,
            body);
        using var firstResponse = await client.SendAsync(firstRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);
        var created = await firstResponse.Content.ReadFromJsonAsync<HostMenuResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(routeName, created.RouteName);

        using var duplicateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/menus",
            adminToken,
            body);
        using var duplicateResponse = await client.SendAsync(
            duplicateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await duplicateResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            IdentityErrorCodes.MenuRouteNameExists,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyCustomMenuLifecycleAndNavigationProjectionAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var routeName = $"menu-{Guid.NewGuid():N}".ToLowerInvariant();

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/menus",
            adminToken,
            CreateSampleMenuRequest(routeName));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<HostMenuResponse>(
            cancellationToken);
        Assert.IsNotNull(created);

        using var navigationRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/navigation");
        navigationRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var navigationResponse = await client.SendAsync(
            navigationRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, navigationResponse.StatusCode);
        using var navigationDocument = JsonDocument.Parse(
            await navigationResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.IsTrue(
            NavigationContainsRouteName(navigationDocument.RootElement, routeName),
            "Custom menu should project into navigation.");

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/menus/{created.Id:D}",
            adminToken,
            new UpdateHostMenuRequest(
                null,
                created.Path,
                created.ComponentKey,
                "已更新菜单",
                created.Caption,
                created.Icon,
                created.DisplayOrder,
                created.RequiredPermission,
                created.Version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<HostMenuResponse>(
            cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual("已更新菜单", updated.Title);

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/menus/{created.Id:D}/disable",
            adminToken,
            new { });
        using var disableResponse = await client.SendAsync(
            disableRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
        var disabled = await disableResponse.Content.ReadFromJsonAsync<HostMenuResponse>(
            cancellationToken);
        Assert.IsNotNull(disabled);
        Assert.IsFalse(disabled.IsActive);

        using var navigationAfterDisable = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/navigation");
        navigationAfterDisable.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var navigationAfterDisableResponse = await client.SendAsync(
            navigationAfterDisable,
            cancellationToken);
        using var navigationAfterDisableDocument = JsonDocument.Parse(
            await navigationAfterDisableResponse.Content.ReadAsStringAsync(
                cancellationToken));
        Assert.IsFalse(
            NavigationContainsRouteName(navigationAfterDisableDocument.RootElement, routeName),
            "Disabled menu should not project into navigation.");
    }

    private static CreateHostMenuRequest CreateSampleMenuRequest(string routeName) =>
        new(
            null,
            routeName,
            "/",
            "overview",
            "集成测试菜单",
            "Integration Menu",
            "grid",
            15,
            IdentityUserManagementPermissions.Read);

    private static bool NavigationContainsRouteName(JsonElement navigation, string routeName)
    {
        foreach (var node in navigation.EnumerateArray())
        {
            if (string.Equals(
                    node.GetProperty("routeName").GetString(),
                    routeName,
                    StringComparison.Ordinal))
            {
                return true;
            }

            if (node.TryGetProperty("children", out var children)
                && NavigationContainsRouteName(children, routeName))
            {
                return true;
            }
        }

        return false;
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
                new LoginRequest("admin", Api.FullNetApiFactory.TestPassword)),
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
