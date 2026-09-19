using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Api;

internal static class TenantEntitlementAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var token = await factory.CreateHostAccessTokenAsync(
            ["tenancy.tenant_entitlements.read"],
            cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/entitlements/catalog");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    public static async Task VerifyEnforcementPhaseRoundTripAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var token = await factory.CreateHostAccessTokenAsync(
            [
                "tenancy.tenant_entitlements.read",
                "tenancy.tenant_entitlements.manage_enforcement",
            ],
            cancellationToken);

        using var getRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/settings/entitlement-enforcement");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var getResponse = await client.SendAsync(getRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);
        var current = await getResponse.Content.ReadFromJsonAsync<TenantEntitlementEnforcementResponse>(
            cancellationToken);
        Assert.IsNotNull(current);

        using var putRequest = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/tenancy/settings/entitlement-enforcement")
        {
            Content = JsonContent.Create(new UpdateTenantEntitlementEnforcementRequest(
                TenantEntitlementEnforcementPhases.Shadow,
                current!.Version)),
        };
        putRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var putResponse = await client.SendAsync(putRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, putResponse.StatusCode);
        var updated = await putResponse.Content.ReadFromJsonAsync<TenantEntitlementEnforcementResponse>(
            cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual(TenantEntitlementEnforcementPhases.Shadow, updated!.Phase);
    }

    public static async Task VerifyCommercialReactivateGateAsync(
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
                "tenancy.tenant_entitlements.manage_enforcement",
                "tenancy.tenant_entitlements.read",
            ],
            cancellationToken);

        using var phaseGet = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/settings/entitlement-enforcement");
        phaseGet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var phaseGetResponse = await client.SendAsync(phaseGet, cancellationToken);
        var phase = await phaseGetResponse.Content.ReadFromJsonAsync<TenantEntitlementEnforcementResponse>(
            cancellationToken);
        Assert.IsNotNull(phase);

        using var phasePut = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/tenancy/settings/entitlement-enforcement")
        {
            Content = JsonContent.Create(new UpdateTenantEntitlementEnforcementRequest(
                TenantEntitlementEnforcementPhases.Enforced,
                phase!.Version)),
        };
        phasePut.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var phasePutResponse = await client.SendAsync(phasePut, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, phasePutResponse.StatusCode);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/tenants?page=1&pageSize=20");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<TenantSummary>>(cancellationToken);
        Assert.IsNotNull(page);
        var tenant = page!.Items.FirstOrDefault(item =>
            item.Identifier == "acme" && item.IsActive && item.TenantPackageId is null);
        if (tenant is null)
        {
            tenant = page.Items.FirstOrDefault(item =>
                item.IsActive && item.TenantPackageId is null);
        }

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

        using var reactivateRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/reactivate")
        {
            Content = JsonContent.Create(new ReactivateTenantRequest(suspended!.Version)),
        };
        reactivateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var reactivateResponse = await client.SendAsync(reactivateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, reactivateResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await reactivateResponse.Content.ReadAsStringAsync(cancellationToken));
        var code = problem.RootElement.GetProperty("code").GetString();
        Assert.AreEqual(TenancyErrorCodes.EntitlementCommercialBindingRequired, code);
    }
}
