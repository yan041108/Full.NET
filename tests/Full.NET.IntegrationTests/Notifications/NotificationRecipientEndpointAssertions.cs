using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>
/// 验证当前用户收件端点的权限、作用域隔离、脱敏、唯一性与删除语义。
/// </summary>
internal static class NotificationRecipientEndpointAssertions
{
    /// <summary>
    /// 在同一数据库夹具中执行 Host 与 Tenant 收件端点 API 契约。
    /// </summary>
    /// <param name="factory">已配置 Notifications 测试 Provider 的 API 工厂。</param>
    /// <param name="cancellationToken">用于取消 HTTP 与数据库验证的令牌。</param>
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var hostClient = factory.CreateClientForHost("localhost");
        using var tenantClient = factory.CreateClientForHost("localhost");
        var hostToken = await NotificationProfileBindingAssertions.LoginAsHostAdminAsync(
            hostClient,
            cancellationToken);
        var tenantSwitchToken = await NotificationProfileBindingAssertions.LoginAsHostAdminAsync(
            tenantClient,
            cancellationToken);

        var hostProfile = await CreatePublishedEndpointProfileAsync(
            hostClient,
            hostToken,
            $"endpoint-host-{Guid.NewGuid():N}"[..24],
            cancellationToken);
        Assert.IsNotNull(hostProfile.LatestPublishedVersionId);

        var rawAddress = $"recipient-{Guid.NewGuid():N}@example.test";
        var readOnlyToken = await factory.CreateHostAccessTokenAsync(
            [NotificationPlatformPermissions.PreferencesRead],
            cancellationToken);
        using var forbiddenRequest = CreateEndpointRequest(
            readOnlyToken,
            hostProfile.LatestPublishedVersionId.Value,
            rawAddress);
        using var forbiddenResponse = await hostClient.SendAsync(
            forbiddenRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        using var createRequest = CreateEndpointRequest(
            hostToken,
            hostProfile.LatestPublishedVersionId.Value,
            rawAddress);
        using var createResponse = await hostClient.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Created,
            createResponse.StatusCode,
            await createResponse.Content.ReadAsStringAsync(cancellationToken));
        var createBody = await createResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.IsFalse(createBody.Contains(rawAddress, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(createBody.Contains("protectedValue", StringComparison.OrdinalIgnoreCase));
        var created = await createResponse.Content.ReadFromJsonAsync<RecipientEndpointResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual("email", created.EndpointKindKey);
        Assert.AreEqual("pending", created.VerificationStatusKey);
        Assert.AreNotEqual(rawAddress, created.MaskedValue);

        using var duplicateRequest = CreateEndpointRequest(
            hostToken,
            hostProfile.LatestPublishedVersionId.Value,
            rawAddress);
        using var duplicateResponse = await hostClient.SendAsync(
            duplicateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/notifications/my-recipient-endpoints");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hostToken);
        using var listResponse = await hostClient.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var listed = await listResponse.Content.ReadFromJsonAsync<RecipientEndpointResponse[]>(
            cancellationToken);
        Assert.IsNotNull(listed);
        Assert.IsTrue(listed.Any(item => item.Id == created.Id));

        var tenantToken = await NotificationProfileBindingAssertions.EnterAcmeTenantAsync(
            tenantClient,
            tenantSwitchToken,
            cancellationToken);
        var tenantProfile = await CreatePublishedEndpointProfileAsync(
            tenantClient,
            tenantToken,
            $"endpoint-tenant-{Guid.NewGuid():N}"[..24],
            cancellationToken);
        Assert.IsNotNull(tenantProfile.LatestPublishedVersionId);

        // ProfileVersion 是数据库全局标识，仍必须与当前受信作用域联合校验，避免 Host 借 ID 写入 Tenant 配置命名空间。
        using var crossScopeRequest = CreateEndpointRequest(
            hostToken,
            tenantProfile.LatestPublishedVersionId.Value,
            $"cross-{Guid.NewGuid():N}@example.test");
        using var crossScopeResponse = await hostClient.SendAsync(
            crossScopeRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, crossScopeResponse.StatusCode);

        using var deleteRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/notifications/my-recipient-endpoints/{created.Id:D}");
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hostToken);
        using var deleteResponse = await hostClient.SendAsync(deleteRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var missingDeleteRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/notifications/my-recipient-endpoints/{created.Id:D}");
        missingDeleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hostToken);
        using var missingDeleteResponse = await hostClient.SendAsync(
            missingDeleteRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, missingDeleteResponse.StatusCode);
    }

    /// <summary>
    /// 创建并发布要求 email 端点的测试渠道配置。
    /// </summary>
    /// <param name="client">当前作用域的 HTTP 客户端。</param>
    /// <param name="token">具备渠道配置管理权限的访问令牌。</param>
    /// <param name="profileKey">当前测试唯一的配置键。</param>
    /// <param name="cancellationToken">用于取消 HTTP 调用的令牌。</param>
    /// <returns>已发布并启用的渠道配置。</returns>
    private static async Task<NotificationProviderProfileResponse> CreatePublishedEndpointProfileAsync(
        HttpClient client,
        string token,
        string profileKey,
        CancellationToken cancellationToken)
    {
        var created = await NotificationProfileBindingAssertions.CreateProfileAsync(
            client,
            token,
            profileKey,
            TestEndpointNotificationProvider.ProviderTypeKeyValue,
            new { endpointBaseUrl = "https://endpoint-provider.test" },
            "vault://test/endpoint-provider-token",
            cancellationToken);
        return await NotificationProfileBindingAssertions.PublishAndEnableAsync(
            client,
            token,
            created,
            cancellationToken);
    }

    /// <summary>
    /// 构造不包含用户、租户和验证状态的当前用户端点登记请求。
    /// </summary>
    /// <param name="token">当前用户访问令牌。</param>
    /// <param name="profileVersionId">当前作用域已发布的渠道配置版本标识。</param>
    /// <param name="rawAddress">仅进入请求体的邮箱原值。</param>
    /// <returns>携带 Bearer 认证的 HTTP 请求。</returns>
    private static HttpRequestMessage CreateEndpointRequest(
        string token,
        Guid profileVersionId,
        string rawAddress) =>
        NotificationProfileBindingAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/my-recipient-endpoints",
            token,
            new
            {
                providerProfileVersionId = profileVersionId,
                endpointKindKey = "email",
                rawValue = rawAddress,
            });
}
