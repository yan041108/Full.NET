using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>验证角色字段授权持久化后会实际约束普通 Host 用户的查询投影。</summary>
internal static class IdentityRoleFieldGrantAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var adminToken = await LoginAsync(
            client,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);

        var catalog = await SendAsync<FieldProjectionResourceDefinition[]>(
            client,
            HttpMethod.Get,
            "/api/v1/identity/field-projections/catalog",
            adminToken,
            null,
            HttpStatusCode.OK,
            cancellationToken);
        var hostUsers = catalog.Single(resource =>
            resource.ResourceKey == FieldProjectionResourceKeys.HostUsers);
        Assert.IsFalse(hostUsers.Fields.Any(field =>
            field.FieldKey.Contains("fn_identity", StringComparison.Ordinal)
            || field.FieldKey.Contains("password", StringComparison.Ordinal)));

        var suffix = Guid.NewGuid().ToString("N");
        var role = await SendAsync<HostRoleResponse>(
            client,
            HttpMethod.Post,
            "/api/v1/identity/roles",
            adminToken,
            new CreateHostRoleRequest($"projection-{suffix}", "字段投影测试角色"),
            HttpStatusCode.Created,
            cancellationToken);
        var grants = await SendAsync<HostRoleFieldGrantsResponse>(
            client,
            HttpMethod.Put,
            $"/api/v1/identity/roles/{role.Id:D}/field-grants",
            adminToken,
            new ReplaceHostRoleFieldGrantsRequest(
                FieldProjectionResourceKeys.HostUsers,
                ["preferred_locale"],
                role.Version),
            HttpStatusCode.OK,
            cancellationToken);
        Assert.AreEqual(role.Version + 1, grants.Version);

        role = await SendAsync<HostRoleResponse>(
            client,
            HttpMethod.Put,
            $"/api/v1/identity/roles/{role.Id:D}/permissions",
            adminToken,
            new ReplaceHostRolePermissionsRequest(
                [IdentityUserManagementPermissions.Read],
                grants.Version),
            HttpStatusCode.OK,
            cancellationToken);

        const string userPassword = "FullNet!2026Projection";
        var username = $"projection-{suffix}";
        var user = await SendAsync<HostUserResponse>(
            client,
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(username, "字段投影测试用户", userPassword),
            HttpStatusCode.Created,
            cancellationToken);
        var userRoles = await SendAsync<HostUserRolesResponse>(
            client,
            HttpMethod.Get,
            $"/api/v1/identity/users/{user.Id:D}/roles",
            adminToken,
            null,
            HttpStatusCode.OK,
            cancellationToken);
        await SendAsync<HostUserRolesResponse>(
            client,
            HttpMethod.Put,
            $"/api/v1/identity/users/{user.Id:D}/roles",
            adminToken,
            new ReplaceHostUserRolesRequest([role.Id], userRoles.Version),
            HttpStatusCode.OK,
            cancellationToken);

        var userToken = await LoginAsync(client, username, userPassword, cancellationToken);
        var page = await SendAsync<PagedResult<HostUserResponse>>(
            client,
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=100",
            userToken,
            null,
            HttpStatusCode.OK,
            cancellationToken);
        var projectedUser = page.Items.Single(item => item.Id == user.Id);
        Assert.IsNotNull(projectedUser.ProjectedFields);
        CollectionAssert.Contains(
            projectedUser.ProjectedFields.EffectiveFieldKeys.ToArray(),
            "preferred_locale");
        CollectionAssert.DoesNotContain(
            projectedUser.ProjectedFields.EffectiveFieldKeys.ToArray(),
            "failed_login_count");
        CollectionAssert.DoesNotContain(
            projectedUser.ProjectedFields.EffectiveFieldKeys.ToArray(),
            "lockout_end_utc");
    }

    private static async Task<string> LoginAsync(
        HttpClient client,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, password)),
        };
        request.Headers.Add("Origin", "http://localhost");
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        Assert.IsNotNull(token);
        return token.AccessToken;
    }

    private static async Task<TResponse> SendAsync<TResponse>(
        HttpClient client,
        HttpMethod method,
        string path,
        string accessToken,
        object? body,
        HttpStatusCode expectedStatus,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(
            expectedStatus,
            response.StatusCode,
            await response.Content.ReadAsStringAsync(cancellationToken));
        var value = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken);
        Assert.IsNotNull(value);
        return value;
    }
}
