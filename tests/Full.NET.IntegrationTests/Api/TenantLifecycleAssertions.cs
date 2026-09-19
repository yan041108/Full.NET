using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Api;

internal static class TenantLifecycleAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var token = await factory.CreateHostAccessTokenAsync(
            [
                "tenancy.host_tenants.read",
                "tenancy.tenant_lifecycle.suspend",
                "tenancy.tenant_lifecycle.reactivate",
            ],
            cancellationToken);
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/tenants?page=1&pageSize=20");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<TenantSummary>>(
            cancellationToken);
        Assert.IsNotNull(page);
        var tenant = page!.Items.FirstOrDefault(item =>
            item.Identifier == "acme" && item.IsActive);
        Assert.IsNotNull(tenant);

        using var suspendRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{tenant!.Id:D}/suspend")
        {
            Content = JsonContent.Create(new SuspendTenantRequest(tenant.Version)),
        };
        suspendRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var suspendResponse = await client.SendAsync(suspendRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, suspendResponse.StatusCode);
        var suspended = await suspendResponse.Content.ReadFromJsonAsync<TenantSummary>(cancellationToken);
        Assert.IsNotNull(suspended);
        Assert.IsFalse(suspended!.IsActive);
        Assert.AreEqual(TenantLifecycleStatuses.Suspended, suspended.LifecycleStatus);

        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        using var blockedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenancy/current");
        using var blockedResponse = await tenantClient.SendAsync(blockedRequest, cancellationToken);
        Assert.IsFalse(
            blockedResponse.IsSuccessStatusCode,
            $"Suspended tenant API should be blocked but returned {blockedResponse.StatusCode}.");

        using var reactivateRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/reactivate")
        {
            Content = JsonContent.Create(new ReactivateTenantRequest(suspended.Version)),
        };
        reactivateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var reactivateResponse = await client.SendAsync(reactivateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, reactivateResponse.StatusCode);
        var reactivated = await reactivateResponse.Content.ReadFromJsonAsync<TenantSummary>(cancellationToken);
        Assert.IsNotNull(reactivated);
        Assert.IsTrue(reactivated!.IsActive);
        Assert.AreEqual(TenantLifecycleStatuses.Active, reactivated.LifecycleStatus);
    }
}