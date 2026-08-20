using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Jobs.Contracts;

namespace Full.NET.IntegrationTests.Jobs;

internal static class JobsHealthReadonlyAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken = default)
    {
        var adminToken = await factory.CreateHostAccessTokenAsync(
            [HostJobPermissions.HealthRead],
            cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/jobs/host-health");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var health = await response.Content.ReadFromJsonAsync<HostJobHealthResponse>(
            cancellationToken);
        Assert.IsNotNull(health);
        Assert.IsTrue(health.RegisteredHandlers.Contains(JobHandlerKinds.Ping));

        var readOnlyToken = await factory.CreateHostAccessTokenAsync(
            [HostJobPermissions.DefinitionsRead],
            cancellationToken);
        using var forbiddenRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/jobs/host-health");
        forbiddenRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            readOnlyToken);
        using var forbiddenResponse = await client.SendAsync(
            forbiddenRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }
}
