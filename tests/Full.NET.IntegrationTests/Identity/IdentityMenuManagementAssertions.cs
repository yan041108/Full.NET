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
        await VerifyPermissionOptionsUseAuthorizationCatalogAsync(factory, client, cancellationToken);
        await VerifyCreateRejectsDuplicateRouteNameAsync(client, cancellationToken);
        await VerifyCustomMenuLifecycleAndNavigationProjectionAsync(client, cancellationToken);
        await VerifyCustomMenuRejectsParentCycleAsync(client, cancellationToken);
        await VerifySystemMenuPresentationUpdateAsync(client, cancellationToken);
        await VerifyExactMenuActionPermissionBoundariesAsync(factory, client, cancellationToken);
        await OpenApiHostMenusContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifyPermissionOptionsUseAuthorizationCatalogAsync(
        Api.FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var deniedRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/menus/permission-options");
        deniedRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await factory.CreateHostAccessTokenAsync(
                ["platform.dashboard.read"],
                cancellationToken));
        using var deniedResponse = await client.SendAsync(deniedRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/menus/permission-options");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        var codes = document.RootElement
            .EnumerateArray()
            .Select(item => item.GetProperty("code").GetString())
            .Where(code => code is not null)
            .ToArray();
        CollectionAssert.Contains(codes, "document.host_documents.read");
        CollectionAssert.Contains(codes, "jobs.schedules.read");
        CollectionAssert.Contains(codes, "notifications.announcements.read");
        CollectionAssert.Contains(codes, "settings.config.read");
        CollectionAssert.DoesNotContain(codes, "identity.super_administrators.manage");
    }

    private static async Task VerifyCustomMenuRejectsParentCycleAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var parentRouteName = $"cycle-parent-{Guid.NewGuid():N}".ToLowerInvariant();
        using var createParentRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/menus",
            adminToken,
            CreateSampleMenuRequest(parentRouteName));
        using var createParentResponse = await client.SendAsync(
            createParentRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createParentResponse.StatusCode);
        var parent = await createParentResponse.Content.ReadFromJsonAsync<HostMenuResponse>(
            cancellationToken);
        Assert.IsNotNull(parent);

        var childRouteName = $"cycle-child-{Guid.NewGuid():N}".ToLowerInvariant();
        var childRequest = CreateSampleMenuRequest(childRouteName) with
        {
            ParentId = parent.Id.ToString("D"),
        };
        using var createChildRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/menus",
            adminToken,
            childRequest);
        using var createChildResponse = await client.SendAsync(
            createChildRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createChildResponse.StatusCode);
        var child = await createChildResponse.Content.ReadFromJsonAsync<HostMenuResponse>(
            cancellationToken);
        Assert.IsNotNull(child);

        using var cycleRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/menus/{parent.Id:D}",
            adminToken,
            new UpdateHostMenuRequest(
                child.Id.ToString("D"),
                parent.Path,
                parent.ComponentKey,
                parent.Title,
                parent.Caption,
                parent.Icon,
                parent.DisplayOrder,
                parent.RequiredPermission,
                parent.Version));
        using var cycleResponse = await client.SendAsync(cycleRequest, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.BadRequest,
            cycleResponse.StatusCode,
            "Custom menu updates must reject descendant parents instead of persisting a cycle.");
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

    private static async Task VerifySystemMenuPresentationUpdateAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/menus?page=1&pageSize=100");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        using var listDocument = JsonDocument.Parse(
            await listResponse.Content.ReadAsStringAsync(cancellationToken));
        var systemMenu = listDocument.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .FirstOrDefault(item =>
                item.GetProperty("isSystem").GetBoolean()
                && string.Equals(
                    item.GetProperty("routeName").GetString(),
                    "overview",
                    StringComparison.Ordinal));
        Assert.AreNotEqual(
            default,
            systemMenu,
            "Seeded system menu 'overview' should be listed.");

        var menuId = systemMenu.GetProperty("id").GetGuid();
        var version = systemMenu.GetProperty("version").GetInt32();
        var path = systemMenu.GetProperty("path").GetString();
        var componentKey = systemMenu.GetProperty("componentKey").GetString();
        var requiredPermission = systemMenu.GetProperty("requiredPermission").GetString();

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/menus/{menuId:D}",
            adminToken,
            new UpdateHostMenuRequest(
                null,
                path!,
                componentKey!,
                "自定义工作台",
                "自定义说明",
                "dashboard",
                5,
                requiredPermission!,
                version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<HostMenuResponse>(
            cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual("自定义工作台", updated.Title);
        Assert.AreEqual("dashboard", updated.Icon);

        using var navigationRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/navigation");
        navigationRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var navigationResponse = await client.SendAsync(
            navigationRequest,
            cancellationToken);
        using var navigationDocument = JsonDocument.Parse(
            await navigationResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.IsTrue(
            NavigationContainsTitle(navigationDocument.RootElement, "自定义工作台"),
            "Navigation should reflect updated system menu title.");
    }

    private static bool NavigationContainsTitle(JsonElement navigation, string title)
    {
        foreach (var node in navigation.EnumerateArray())
        {
            if (string.Equals(
                    node.GetProperty("title").GetString(),
                    title,
                    StringComparison.Ordinal))
            {
                return true;
            }

            if (node.TryGetProperty("children", out var children)
                && NavigationContainsTitle(children, title))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task VerifyExactMenuActionPermissionBoundariesAsync(
        Api.FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var routeName = $"boundary-{Guid.NewGuid():N}".ToLowerInvariant();

        using var createTargetRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/menus",
            adminToken,
            CreateSampleMenuRequest(routeName));
        using var createTargetResponse = await client.SendAsync(createTargetRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createTargetResponse.StatusCode);
        var targetMenu = await createTargetResponse.Content.ReadFromJsonAsync<HostMenuResponse>(
            cancellationToken);
        Assert.IsNotNull(targetMenu);

        var readOnlyToken = await factory.CreateHostAccessTokenAsync(
            [IdentityMenuManagementPermissions.Read],
            cancellationToken);
        await AssertMenusListAllowedAsync(client, readOnlyToken, cancellationToken);
        await AssertMenuPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            "/api/v1/identity/menus",
            CreateSampleMenuRequest($"denied-{Guid.NewGuid():N}"),
            cancellationToken);
        await AssertMenuPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Put,
            $"/api/v1/identity/menus/{targetMenu.Id:D}",
            new UpdateHostMenuRequest(
                null,
                targetMenu.Path,
                targetMenu.ComponentKey,
                "拒绝更新",
                targetMenu.Caption,
                targetMenu.Icon,
                targetMenu.DisplayOrder,
                targetMenu.RequiredPermission,
                targetMenu.Version),
            cancellationToken);
        await AssertMenuPermissionDeniedAsync<object?>(
            client,
            readOnlyToken,
            HttpMethod.Post,
            $"/api/v1/identity/menus/{targetMenu.Id:D}/disable",
            null,
            cancellationToken);

        var createToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityMenuManagementPermissions.Read,
                IdentityMenuManagementPermissions.Create,
            ],
            cancellationToken);
        var createdByLimited = await CreateHostMenuWithTokenAsync(
            client,
            createToken,
            $"limited-{Guid.NewGuid():N}",
            cancellationToken);
        await AssertMenuPermissionDeniedAsync(
            client,
            createToken,
            HttpMethod.Put,
            $"/api/v1/identity/menus/{targetMenu.Id:D}",
            new UpdateHostMenuRequest(
                null,
                targetMenu.Path,
                targetMenu.ComponentKey,
                "拒绝更新",
                targetMenu.Caption,
                targetMenu.Icon,
                targetMenu.DisplayOrder,
                targetMenu.RequiredPermission,
                targetMenu.Version),
            cancellationToken);

        var updateToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityMenuManagementPermissions.Read,
                IdentityMenuManagementPermissions.Update,
            ],
            cancellationToken);
        await AssertMenuOkAsync(
            client,
            updateToken,
            HttpMethod.Put,
            $"/api/v1/identity/menus/{targetMenu.Id:D}",
            new UpdateHostMenuRequest(
                null,
                targetMenu.Path,
                targetMenu.ComponentKey,
                "受限更新标题",
                targetMenu.Caption,
                targetMenu.Icon,
                targetMenu.DisplayOrder,
                targetMenu.RequiredPermission,
                targetMenu.Version),
            cancellationToken);
        await AssertMenuPermissionDeniedAsync(
            client,
            updateToken,
            HttpMethod.Post,
            "/api/v1/identity/menus",
            CreateSampleMenuRequest($"denied-update-{Guid.NewGuid():N}"),
            cancellationToken);

        var disableTarget = await CreateHostMenuWithTokenAsync(
            client,
            adminToken,
            $"disable-target-{Guid.NewGuid():N}",
            cancellationToken);
        var disableToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityMenuManagementPermissions.Read,
                IdentityMenuManagementPermissions.Disable,
            ],
            cancellationToken);
        await AssertMenuOkAsync<object?>(
            client,
            disableToken,
            HttpMethod.Post,
            $"/api/v1/identity/menus/{disableTarget.Id:D}/disable",
            null,
            cancellationToken);
        await AssertMenuPermissionDeniedAsync(
            client,
            disableToken,
            HttpMethod.Put,
            $"/api/v1/identity/menus/{createdByLimited.Id:D}",
            new UpdateHostMenuRequest(
                null,
                createdByLimited.Path,
                createdByLimited.ComponentKey,
                "拒绝更新",
                createdByLimited.Caption,
                createdByLimited.Icon,
                createdByLimited.DisplayOrder,
                createdByLimited.RequiredPermission,
                createdByLimited.Version),
            cancellationToken);
    }

    private static async Task<HostMenuResponse> CreateHostMenuWithTokenAsync(
        HttpClient client,
        string accessToken,
        string routeName,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/menus",
            accessToken,
            CreateSampleMenuRequest(routeName));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<HostMenuResponse>(cancellationToken);
        Assert.IsNotNull(created);
        return created;
    }

    private static async Task AssertMenusListAllowedAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/menus?page=1&pageSize=20");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertMenuPermissionDeniedAsync<TRequest>(
        HttpClient client,
        string accessToken,
        HttpMethod method,
        string path,
        TRequest? body,
        CancellationToken cancellationToken)
    {
        using var request = body is null
            ? new HttpRequestMessage(method, path)
            : CreateBearerJsonRequest(method, path, accessToken, body);
        if (body is null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "authorization.permission_denied",
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task AssertMenuOkAsync<TRequest>(
        HttpClient client,
        string accessToken,
        HttpMethod method,
        string path,
        TRequest? body,
        CancellationToken cancellationToken)
    {
        using var request = body is null
            ? new HttpRequestMessage(method, path)
            : CreateBearerJsonRequest(method, path, accessToken, body);
        if (body is null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        Assert.IsTrue(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"Expected success but received {(int)response.StatusCode}");
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
