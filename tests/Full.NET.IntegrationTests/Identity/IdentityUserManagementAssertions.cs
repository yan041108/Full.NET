using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>
/// Host 用户管理纵向切片验收夹具；端点未实现时应保持失败（RED）。
/// </summary>
internal static class IdentityUserManagementAssertions
{
    public static async Task VerifyHostUserManagementContractAsync(
        Api.FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionAsync(
            factory,
            client,
            cancellationToken);
        await VerifyCreateRejectsDuplicateUsernameAsync(
            factory,
            client,
            cancellationToken);
        await VerifyDisabledUserCannotLoginAsync(
            factory,
            client,
            cancellationToken);
        await VerifyCannotDisableLastRemainingSuperAdministratorAsync(
            client,
            cancellationToken);
        await VerifyUpdateDisplayNameWithOptimisticVersionAsync(
            client,
            cancellationToken);
        await VerifyResetPasswordInvalidatesOldCredentialsAsync(
            client,
            cancellationToken);
        await VerifyEnableUserRestoresLoginAsync(
            client,
            cancellationToken);
        await VerifyExactActionPermissionBoundariesAsync(
            factory,
            client,
            cancellationToken);
        await OpenApiHostUsersContractAssertions.VerifyAsync(
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
            "/api/v1/identity/users?page=1&pageSize=20");
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

    private static async Task VerifyCreateRejectsDuplicateUsernameAsync(
        Api.FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var username = $"host-user-{Guid.NewGuid():N}";
        var body = new CreateHostUserRequest(
            username,
            "集成测试用户",
            Api.FullNetApiFactory.TestPassword);

        using var firstRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            body);
        using var firstResponse = await client.SendAsync(firstRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);
        var created = await firstResponse.Content.ReadFromJsonAsync<HostUserResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(username, created.Username);

        using var duplicateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            body);
        using var duplicateResponse = await client.SendAsync(
            duplicateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await duplicateResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            IdentityErrorCodes.UsernameExists,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyDisabledUserCannotLoginAsync(
        Api.FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var username = $"disabled-{Guid.NewGuid():N}";
        var password = Api.FullNetApiFactory.TestPassword;

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(username, "待禁用用户", password));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<HostUserResponse>(
            cancellationToken);
        Assert.IsNotNull(created);

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/users/{created.Id:D}/disable",
            adminToken,
            new { });
        using var disableResponse = await client.SendAsync(
            disableRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);

        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, password)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await loginResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            IdentityErrorCodes.InvalidCredentials,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyCannotDisableLastRemainingSuperAdministratorAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=50");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<HostUserResponse>>(
            cancellationToken);
        Assert.IsNotNull(page);
        var admin = page.Items.Single(user => user.Username == "admin");

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/users/{admin.Id:D}/disable",
            adminToken,
            new { });
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, disableResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await disableResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            IdentityErrorCodes.SuperAdministratorLastRemaining,
            problem.RootElement.GetProperty("code").GetString());

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
    }

    private static async Task VerifyUpdateDisplayNameWithOptimisticVersionAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var username = $"update-{Guid.NewGuid():N}";

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(
                username,
                "更新前名称",
                Api.FullNetApiFactory.TestPassword));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<HostUserResponse>(
            cancellationToken);
        Assert.IsNotNull(created);

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/users/{created.Id:D}",
            adminToken,
            new UpdateHostUserRequest("更新后名称", created.Version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<HostUserResponse>(
            cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual("更新后名称", updated.DisplayName);
        Assert.AreEqual(created.Version + 1, updated.Version);

        using var staleRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/users/{created.Id:D}",
            adminToken,
            new UpdateHostUserRequest("冲突名称", created.Version));
        using var staleResponse = await client.SendAsync(staleRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, staleResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await staleResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            IdentityErrorCodes.ProfileVersionConflict,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyResetPasswordInvalidatesOldCredentialsAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var username = $"reset-{Guid.NewGuid():N}";
        const string originalPassword = "FullNet!2026Secure";
        const string newPassword = "FullNet!2026Rotate";

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(username, "重置密码测试", originalPassword));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<HostUserResponse>(
            cancellationToken);
        Assert.IsNotNull(created);

        using var loginBeforeRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, originalPassword)),
        };
        loginBeforeRequest.Headers.Add("Origin", "http://localhost");
        using var loginBeforeResponse = await client.SendAsync(loginBeforeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginBeforeResponse.StatusCode);

        using var resetRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/users/{created.Id:D}/reset-password",
            adminToken,
            new ResetHostUserPasswordRequest(newPassword));
        using var resetResponse = await client.SendAsync(resetRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, resetResponse.StatusCode);

        using var oldLoginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, originalPassword)),
        };
        oldLoginRequest.Headers.Add("Origin", "http://localhost");
        using var oldLoginResponse = await client.SendAsync(oldLoginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, oldLoginResponse.StatusCode);

        using var newLoginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, newPassword)),
        };
        newLoginRequest.Headers.Add("Origin", "http://localhost");
        using var newLoginResponse = await client.SendAsync(newLoginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, newLoginResponse.StatusCode);
    }

    private static async Task VerifyEnableUserRestoresLoginAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var username = $"enabled-{Guid.NewGuid():N}";
        var password = Api.FullNetApiFactory.TestPassword;

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(username, "启用测试", password));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<HostUserResponse>(
            cancellationToken);
        Assert.IsNotNull(created);

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/users/{created.Id:D}/disable",
            adminToken,
            new { });
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);

        using var loginWhileDisabledRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, password)),
        };
        loginWhileDisabledRequest.Headers.Add("Origin", "http://localhost");
        using var loginWhileDisabledResponse = await client.SendAsync(
            loginWhileDisabledRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, loginWhileDisabledResponse.StatusCode);

        using var enableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/users/{created.Id:D}/enable",
            adminToken,
            new { });
        using var enableResponse = await client.SendAsync(enableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, enableResponse.StatusCode);
        var enabled = await enableResponse.Content.ReadFromJsonAsync<HostUserResponse>(
            cancellationToken);
        Assert.IsNotNull(enabled);
        Assert.IsTrue(enabled.IsActive);
        Assert.AreEqual(created.Version + 2, enabled.Version);

        using var loginAfterEnableRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, password)),
        };
        loginAfterEnableRequest.Headers.Add("Origin", "http://localhost");
        using var loginAfterEnableResponse = await client.SendAsync(
            loginAfterEnableRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginAfterEnableResponse.StatusCode);
    }

    private static async Task VerifyExactActionPermissionBoundariesAsync(
        Api.FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var activeUsername = $"action-active-{Guid.NewGuid():N}";
        var disabledUsername = $"action-disabled-{Guid.NewGuid():N}";
        var password = Api.FullNetApiFactory.TestPassword;

        var activeUser = await CreateHostUserAsync(
            client,
            adminToken,
            activeUsername,
            "动作边界活跃用户",
            password,
            cancellationToken);
        var disabledUser = await CreateHostUserAsync(
            client,
            adminToken,
            disabledUsername,
            "动作边界禁用用户",
            password,
            cancellationToken);
        await PostWithoutBodyAsync(
            client,
            adminToken,
            $"/api/v1/identity/users/{disabledUser.Id:D}/disable",
            HttpStatusCode.OK,
            cancellationToken);

        var readOnlyToken = await factory.CreateHostAccessTokenAsync(
            [IdentityUserManagementPermissions.Read],
            cancellationToken);
        await AssertUsersListAllowedAsync(client, readOnlyToken, cancellationToken);
        await AssertPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            "/api/v1/identity/users",
            new CreateHostUserRequest(
                $"denied-{Guid.NewGuid():N}",
                "拒绝创建",
                password),
            cancellationToken);
        await AssertPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Put,
            $"/api/v1/identity/users/{activeUser.Id:D}",
            new UpdateHostUserRequest("拒绝更新", activeUser.Version),
            cancellationToken);
        await AssertPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            $"/api/v1/identity/users/{activeUser.Id:D}/disable",
            new { },
            cancellationToken);
        await AssertPermissionDeniedAsync<object?>(
            client,
            readOnlyToken,
            HttpMethod.Get,
            "/api/v1/identity/users/export",
            null,
            cancellationToken);

        var createToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityUserManagementPermissions.Read,
                IdentityUserManagementPermissions.Create,
            ],
            cancellationToken);
        var createdByLimited = await CreateHostUserAsync(
            client,
            createToken,
            $"limited-create-{Guid.NewGuid():N}",
            "受限创建用户",
            password,
            cancellationToken);
        await AssertPermissionDeniedAsync(
            client,
            createToken,
            HttpMethod.Post,
            $"/api/v1/identity/users/{createdByLimited.Id:D}/disable",
            new { },
            cancellationToken);

        var updateToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityUserManagementPermissions.Read,
                IdentityUserManagementPermissions.Update,
            ],
            cancellationToken);
        await AssertOkAsync(
            client,
            updateToken,
            HttpMethod.Put,
            $"/api/v1/identity/users/{activeUser.Id:D}",
            new UpdateHostUserRequest("受限更新名称", activeUser.Version),
            cancellationToken);
        await AssertPermissionDeniedAsync(
            client,
            updateToken,
            HttpMethod.Post,
            "/api/v1/identity/users",
            new CreateHostUserRequest(
                $"denied-update-{Guid.NewGuid():N}",
                "拒绝创建",
                password),
            cancellationToken);

        var disableToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityUserManagementPermissions.Read,
                IdentityUserManagementPermissions.Disable,
            ],
            cancellationToken);
        var disableTarget = await CreateHostUserAsync(
            client,
            adminToken,
            $"disable-target-{Guid.NewGuid():N}",
            "禁用目标",
            password,
            cancellationToken);
        await PostWithoutBodyAsync(
            client,
            disableToken,
            $"/api/v1/identity/users/{disableTarget.Id:D}/disable",
            HttpStatusCode.OK,
            cancellationToken);
        await AssertPermissionDeniedAsync(
            client,
            disableToken,
            HttpMethod.Post,
            $"/api/v1/identity/users/{disabledUser.Id:D}/enable",
            new { },
            cancellationToken);

        var enableToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityUserManagementPermissions.Read,
                IdentityUserManagementPermissions.Enable,
            ],
            cancellationToken);
        await PostWithoutBodyAsync(
            client,
            enableToken,
            $"/api/v1/identity/users/{disabledUser.Id:D}/enable",
            HttpStatusCode.OK,
            cancellationToken);
        await AssertPermissionDeniedAsync(
            client,
            enableToken,
            HttpMethod.Post,
            $"/api/v1/identity/users/{activeUser.Id:D}/disable",
            new { },
            cancellationToken);

        var resetToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityUserManagementPermissions.Read,
                IdentityUserManagementPermissions.ResetPassword,
            ],
            cancellationToken);
        var resetTarget = await CreateHostUserAsync(
            client,
            adminToken,
            $"reset-target-{Guid.NewGuid():N}",
            "重置密码目标",
            password,
            cancellationToken);
        await PostWithoutBodyAsync(
            client,
            resetToken,
            $"/api/v1/identity/users/{resetTarget.Id:D}/reset-password",
            HttpStatusCode.OK,
            cancellationToken,
            new ResetHostUserPasswordRequest("FullNet!2026Rotate"));
        await AssertPermissionDeniedAsync(
            client,
            resetToken,
            HttpMethod.Put,
            $"/api/v1/identity/users/{resetTarget.Id:D}",
            new UpdateHostUserRequest("拒绝更新", resetTarget.Version),
            cancellationToken);

        var exportToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityUserManagementPermissions.Read,
                IdentityUserManagementPermissions.Export,
            ],
            cancellationToken);
        await AssertOkAsync<object?>(
            client,
            exportToken,
            HttpMethod.Get,
            "/api/v1/identity/users/export",
            null,
            cancellationToken);
        await AssertPermissionDeniedAsync(
            client,
            exportToken,
            HttpMethod.Post,
            "/api/v1/identity/users",
            new CreateHostUserRequest(
                $"denied-export-{Guid.NewGuid():N}",
                "拒绝创建",
                password),
            cancellationToken);
    }

    private static async Task<HostUserResponse> CreateHostUserAsync(
        HttpClient client,
        string accessToken,
        string username,
        string displayName,
        string password,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            accessToken,
            new CreateHostUserRequest(username, displayName, password));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<HostUserResponse>(cancellationToken);
        Assert.IsNotNull(created);
        return created;
    }

    private static async Task AssertUsersListAllowedAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=20");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertPermissionDeniedAsync<TRequest>(
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

    private static async Task AssertOkAsync<TRequest>(
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
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task PostWithoutBodyAsync(
        HttpClient client,
        string accessToken,
        string path,
        HttpStatusCode expectedStatus,
        CancellationToken cancellationToken,
        object? body = null)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            path,
            accessToken,
            body ?? new { });
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(expectedStatus, response.StatusCode);
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
