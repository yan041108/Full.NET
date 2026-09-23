using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Api;

internal static class TenantSubscriptionAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var token = await factory.CreateHostAccessTokenAsync(
            [
                "tenancy.tenant_subscriptions.read",
                "tenancy.tenant_subscriptions.manage",
                "tenancy.host_tenants.read",
                "tenancy.tenant_packages.create",
                "tenancy.tenants.create",
            ],
            cancellationToken);

        var packageId = await CreatePackageAsync(client, token, cancellationToken);
        var tenant = await CreateTenantAsync(client, token, cancellationToken);
        Assert.IsNull(tenant.TenantPackageId);

        var periodStart = DateTimeOffset.UtcNow;
        var periodEnd = periodStart.AddDays(30);
        using var createRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/subscriptions")
        {
            Content = JsonContent.Create(new CreateTenantSubscriptionRequest(
                packageId,
                TenantSubscriptionStatuses.Trial,
                periodEnd,
                periodStart,
                periodEnd)),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TenantSubscriptionResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(TenantSubscriptionStatuses.Trial, created!.Status);
        Assert.AreEqual(packageId, created.PackageId);

        using var getTenantRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}");
        getTenantRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var getTenantResponse = await client.SendAsync(getTenantRequest, cancellationToken);
        var tenantAfterCreate = await getTenantResponse.Content.ReadFromJsonAsync<TenantSummary>(
            cancellationToken);
        Assert.IsNotNull(tenantAfterCreate);
        Assert.AreEqual(packageId, tenantAfterCreate!.TenantPackageId);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/subscriptions");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<IReadOnlyList<TenantSubscriptionResponse>>(
            cancellationToken);
        Assert.IsNotNull(list);
        Assert.IsTrue(list!.Any(item => item.Id == created.Id));

        using var cancelRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/subscriptions/{created.Id:D}/cancel")
        {
            Content = JsonContent.Create(new CancelTenantSubscriptionRequest(created.Version)),
        };
        cancelRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var cancelResponse = await client.SendAsync(cancelRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelled = await cancelResponse.Content.ReadFromJsonAsync<TenantSubscriptionResponse>(
            cancellationToken);
        Assert.IsNotNull(cancelled);
        Assert.AreEqual(TenantSubscriptionStatuses.Cancelled, cancelled!.Status);
        Assert.IsNotNull(cancelled.CancelledAtUtc);
    }

    private static async Task<Guid> CreatePackageAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        var code = "sub-" + Guid.NewGuid().ToString("N")[..8];
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/tenancy/tenant-packages")
        {
            Content = JsonContent.Create(new CreateHostTenantPackageRequest(
                code,
                "订阅联动套餐",
                null)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var package = await response.Content.ReadFromJsonAsync<TenantPackageSummary>(cancellationToken);
        Assert.IsNotNull(package);
        return package!.Id;
    }

    private static async Task<TenantSummary> CreateTenantAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        var identifier = $"sub-{Guid.NewGuid():N}"[..14];
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/tenancy/tenants")
        {
            Content = JsonContent.Create(new ProvisionTenantRequest(
                identifier,
                "订阅测试租户",
                $"{identifier}.localhost")),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var tenant = await response.Content.ReadFromJsonAsync<TenantSummary>(cancellationToken);
        Assert.IsNotNull(tenant);
        return tenant!;
    }
}
