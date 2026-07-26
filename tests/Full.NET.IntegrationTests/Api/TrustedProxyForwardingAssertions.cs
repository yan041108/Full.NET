using System.Net;
using System.Net.Http.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>
/// 验证可信代理规范化结果同时进入 Origin 校验与认证审计。
/// </summary>
internal static class TrustedProxyForwardingAssertions
{
    private const string ForwardedClientAddress = "198.51.100.42";

    public static async Task VerifyAuthenticationAuditAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        var auditCountBefore = await factory.GetAuthenticationAuditCountByIpAddressAsync(
            ForwardedClientAddress,
            cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new
            {
                username = $"missing-{Guid.NewGuid():N}",
                password = FullNetApiFactory.TestPassword,
            }),
        };
        request.Headers.Add("Origin", "https://localhost");
        request.Headers.Add("X-Forwarded-For", ForwardedClientAddress);
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            response.StatusCode,
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            auditCountBefore + 1,
            await factory.GetAuthenticationAuditCountByIpAddressAsync(
                ForwardedClientAddress,
                cancellationToken));
    }
}
