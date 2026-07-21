using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>
/// Host 菜单管理纵向切片验收夹具；端点未实现时应保持失败（RED）。
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
}
