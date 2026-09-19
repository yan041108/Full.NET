using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Api;

internal static class TenantQuotaAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var token = await factory.CreateHostAccessTokenAsync(
            [
                "tenancy.tenant_quota.read",
                "tenancy.tenant_quota.manage",
                "tenancy.tenant_quota.reserve",
                "tenancy.host_tenants.read",
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
        var tenant = page!.Items.First(item => item.Identifier == "acme");

        using var metricsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/quota/metrics");
        metricsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var metricsResponse = await client.SendAsync(metricsRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, metricsResponse.StatusCode);
        var metrics = await metricsResponse.Content.ReadFromJsonAsync<IReadOnlyList<TenantQuotaMetricResponse>>(
            cancellationToken);
        Assert.IsNotNull(metrics);
        var seeded = metrics!.FirstOrDefault(metric =>
            metric.MetricCode == TenantQuotaMetricCodes.IdentitySeats
            && metric.PeriodKey == TenantQuotaDefaults.PeriodKey);
        Assert.IsNotNull(seeded);
        Assert.AreEqual(TenantQuotaDefaults.IdentitySeatsLimit, seeded!.LimitValue);

        using var upsertRequest = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/quota/metrics")
        {
            Content = JsonContent.Create(new UpsertTenantQuotaMetricRequest(
                TenantQuotaMetricCodes.IdentitySeats,
                TenantQuotaDefaults.PeriodKey,
                200)),
        };
        upsertRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var upsertResponse = await client.SendAsync(upsertRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, upsertResponse.StatusCode);
        var metric = await upsertResponse.Content.ReadFromJsonAsync<TenantQuotaMetricResponse>(
            cancellationToken);
        Assert.IsNotNull(metric);
        Assert.AreEqual(200, metric!.LimitValue);

        using var limitOneRequest = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/quota/metrics")
        {
            Content = JsonContent.Create(new UpsertTenantQuotaMetricRequest(
                TenantQuotaMetricCodes.IdentitySeats,
                TenantQuotaDefaults.PeriodKey,
                1)),
        };
        limitOneRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var limitOneResponse = await client.SendAsync(limitOneRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, limitOneResponse.StatusCode);

        using var reserveOneRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/quota/reserve")
        {
            Content = JsonContent.Create(new ReserveTenantQuotaRequest(
                TenantQuotaMetricCodes.IdentitySeats,
                Guid.CreateVersion7().ToString("D"),
                1)),
        };
        reserveOneRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var reserveOneResponse = await client.SendAsync(reserveOneRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, reserveOneResponse.StatusCode);

        using var reserveTwoRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/quota/reserve")
        {
            Content = JsonContent.Create(new ReserveTenantQuotaRequest(
                TenantQuotaMetricCodes.IdentitySeats,
                Guid.CreateVersion7().ToString("D"),
                1)),
        };
        reserveTwoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var reserveTwoResponse = await client.SendAsync(reserveTwoRequest, cancellationToken);
        Assert.AreNotEqual(HttpStatusCode.OK, reserveTwoResponse.StatusCode);
    }
}