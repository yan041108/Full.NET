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
        var catalog = await response.Content.ReadFromJsonAsync<
            IReadOnlyList<TenantEntitlementCatalogResponse>>(cancellationToken);
        Assert.IsNotNull(catalog);
        Assert.IsTrue(catalog!.Any(item =>
            string.Equals(
                item.Code,
                TenantEntitlementCatalogCodes.Workflow,
                StringComparison.Ordinal)));
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

    public static async Task VerifyEnforcementPhaseProgressionAsync(
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

        var current = await GetEnforcementPhaseAsync(client, token, cancellationToken);
        Assert.IsNotNull(current);
        if (!string.Equals(
                current!.Phase,
                TenantEntitlementEnforcementPhases.Compatibility,
                StringComparison.Ordinal))
        {
            current = await PutEnforcementPhaseAsync(
                client,
                token,
                TenantEntitlementEnforcementPhases.Compatibility,
                current.Version,
                cancellationToken);
        }

        var shadow = await PutEnforcementPhaseAsync(
            client,
            token,
            TenantEntitlementEnforcementPhases.Shadow,
            current!.Version,
            cancellationToken);
        Assert.AreEqual(TenantEntitlementEnforcementPhases.Shadow, shadow!.Phase);

        var enforced = await PutEnforcementPhaseAsync(
            client,
            token,
            TenantEntitlementEnforcementPhases.Enforced,
            shadow.Version,
            cancellationToken);
        Assert.AreEqual(TenantEntitlementEnforcementPhases.Enforced, enforced!.Phase);

        var finalPhase = await GetEnforcementPhaseAsync(client, token, cancellationToken);
        Assert.IsNotNull(finalPhase);
        Assert.AreEqual(TenantEntitlementEnforcementPhases.Enforced, finalPhase!.Phase);
    }

    private static async Task<TenantEntitlementEnforcementResponse?> GetEnforcementPhaseAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var getRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/settings/entitlement-enforcement");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var getResponse = await client.SendAsync(getRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);
        return await getResponse.Content.ReadFromJsonAsync<TenantEntitlementEnforcementResponse>(
            cancellationToken);
    }

    private static async Task<TenantEntitlementEnforcementResponse?> PutEnforcementPhaseAsync(
        HttpClient client,
        string token,
        string phase,
        int version,
        CancellationToken cancellationToken)
    {
        using var putRequest = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/tenancy/settings/entitlement-enforcement")
        {
            Content = JsonContent.Create(new UpdateTenantEntitlementEnforcementRequest(phase, version)),
        };
        putRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var putResponse = await client.SendAsync(putRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, putResponse.StatusCode);
        return await putResponse.Content.ReadFromJsonAsync<TenantEntitlementEnforcementResponse>(
            cancellationToken);
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

    public static async Task VerifyCommercialReactivateSucceedsWithActiveSubscriptionAsync(
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
                "tenancy.tenant_subscriptions.manage",
                "tenancy.tenant_packages.create",
                "tenancy.tenants.create",
            ],
            cancellationToken);

        var phase = await GetEnforcementPhaseAsync(client, token, cancellationToken);
        Assert.IsNotNull(phase);
        var enforced = await PutEnforcementPhaseAsync(
            client,
            token,
            TenantEntitlementEnforcementPhases.Enforced,
            phase!.Version,
            cancellationToken);
        Assert.AreEqual(TenantEntitlementEnforcementPhases.Enforced, enforced!.Phase);

        var packageId = await CreateSubscriptionProbePackageAsync(client, token, cancellationToken);
        var tenant = await CreateSubscriptionProbeTenantAsync(client, token, cancellationToken);
        var periodStart = DateTimeOffset.UtcNow;
        var periodEnd = periodStart.AddDays(30);
        using var subscriptionRequest = new HttpRequestMessage(
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
        subscriptionRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var subscriptionResponse = await client.SendAsync(subscriptionRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, subscriptionResponse.StatusCode);

        var currentTenant = await FindHostTenantByIdAsync(client, token, tenant.Id, cancellationToken);

        using var suspendRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/suspend")
        {
            Content = JsonContent.Create(new SuspendTenantRequest(currentTenant.Version)),
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
        Assert.AreEqual(HttpStatusCode.OK, reactivateResponse.StatusCode);
        var reactivated = await reactivateResponse.Content.ReadFromJsonAsync<TenantSummary>(cancellationToken);
        Assert.IsNotNull(reactivated);
        Assert.IsTrue(reactivated!.IsActive);
        Assert.AreEqual(TenantLifecycleStatuses.Active, reactivated.LifecycleStatus);
    }

    private static async Task<Guid> CreateSubscriptionProbePackageAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        var code = "react-" + Guid.NewGuid().ToString("N")[..8];
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/tenancy/tenant-packages")
        {
            Content = JsonContent.Create(new CreateHostTenantPackageRequest(
                code,
                "Reactivate gate probe package",
                null)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var package = await response.Content.ReadFromJsonAsync<TenantPackageSummary>(cancellationToken);
        Assert.IsNotNull(package);
        return package!.Id;
    }

    private static async Task<TenantSummary> CreateSubscriptionProbeTenantAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        var identifier = $"rg-{Guid.NewGuid():N}"[..12];
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/tenancy/tenants")
        {
            Content = JsonContent.Create(new ProvisionTenantRequest(
                identifier,
                "Reactivate Gate Probe",
                $"{identifier}.localhost")),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var tenant = await response.Content.ReadFromJsonAsync<TenantSummary>(cancellationToken);
        Assert.IsNotNull(tenant);
        return tenant!;
    }

    public static async Task VerifyEntitlementBackfillDryRunAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var token = await factory.CreateHostAccessTokenAsync(
            [
                "tenancy.tenant_entitlements.read",
                "tenancy.tenant_entitlements.reconcile_backfill",
                "tenancy.tenant_entitlements.manage_catalog",
            ],
            cancellationToken);

        await EnsureCompatibilityBaselineCatalogAsync(client, token, cancellationToken);

        using var backfillRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/tenancy/entitlements/backfill")
        {
            Content = JsonContent.Create(new TenantEntitlementBackfillRequest(DryRun: true)),
        };
        backfillRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var backfillResponse = await client.SendAsync(backfillRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, backfillResponse.StatusCode);
        var result = await backfillResponse.Content.ReadFromJsonAsync<TenantEntitlementBackfillResponse>(
            cancellationToken);
        Assert.IsNotNull(result);
        Assert.IsTrue(result!.DryRun);
        Assert.AreEqual(0, result.AppliedCount);
        Assert.IsTrue(result.MissingBindingCount >= 0);
    }

    public static async Task VerifyEntitlementBackfillApplyIsIdempotentAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var token = await factory.CreateHostAccessTokenAsync(
            [
                "tenancy.tenant_entitlements.read",
                "tenancy.tenant_entitlements.reconcile_backfill",
                "tenancy.tenant_entitlements.manage_catalog",
            ],
            cancellationToken);

        await EnsureCompatibilityBaselineCatalogAsync(client, token, cancellationToken);

        var dryRun = await PostBackfillAsync(client, token, dryRun: true, cancellationToken);
        Assert.IsNotNull(dryRun);
        if (dryRun!.MissingBindingCount == 0)
        {
            return;
        }

        var applied = await PostBackfillAsync(client, token, dryRun: false, cancellationToken);
        Assert.IsNotNull(applied);
        Assert.IsFalse(applied!.DryRun);
        Assert.AreEqual(dryRun.MissingBindingCount, applied.AppliedCount);

        var afterApply = await PostBackfillAsync(client, token, dryRun: true, cancellationToken);
        Assert.IsNotNull(afterApply);
        Assert.IsTrue(afterApply!.MissingBindingCount < dryRun.MissingBindingCount);
    }

    public static async Task VerifyEntitlementBackfillThenEnforcementProgressionAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await VerifyEntitlementBackfillApplyIsIdempotentAsync(factory, cancellationToken);
        await VerifyEnforcementPhaseProgressionAsync(factory, cancellationToken);
    }

    public static async Task VerifyEnforcedUnboundTenantHasEmptyBindingsAndBlocksReactivateAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var token = await factory.CreateHostAccessTokenAsync(
            [
                "tenancy.tenants.create",
                "tenancy.host_tenants.read",
                "tenancy.tenant_lifecycle.suspend",
                "tenancy.tenant_lifecycle.reactivate",
                "tenancy.tenant_entitlements.read",
                "tenancy.tenant_entitlements.manage_enforcement",
            ],
            cancellationToken);

        var phase = await GetEnforcementPhaseAsync(client, token, cancellationToken);
        Assert.IsNotNull(phase);
        var enforced = await PutEnforcementPhaseAsync(
            client,
            token,
            TenantEntitlementEnforcementPhases.Enforced,
            phase!.Version,
            cancellationToken);
        Assert.AreEqual(TenantEntitlementEnforcementPhases.Enforced, enforced!.Phase);

        var tenant = await CreateSubscriptionProbeTenantAsync(client, token, cancellationToken);
        Assert.IsNull(tenant.TenantPackageId);

        using var bindingsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/entitlements");
        bindingsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var bindingsResponse = await client.SendAsync(bindingsRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, bindingsResponse.StatusCode);
        var bindings = await bindingsResponse.Content.ReadFromJsonAsync<
            IReadOnlyList<TenantEntitlementBindingResponse>>(cancellationToken);
        Assert.IsNotNull(bindings);
        Assert.AreEqual(0, bindings!.Count);

        using var suspendRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/suspend")
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
        Assert.AreEqual(
            TenancyErrorCodes.EntitlementCommercialBindingRequired,
            problem.RootElement.GetProperty("code").GetString());
    }

    /// <summary>Baseline 种子通常已登记 compatibility.baseline；仅在缺失时经 API 补建。</summary>
    private static async Task EnsureCompatibilityBaselineCatalogAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/entitlements/catalog");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var catalog = await listResponse.Content.ReadFromJsonAsync<
            IReadOnlyList<TenantEntitlementCatalogResponse>>(cancellationToken);
        Assert.IsNotNull(catalog);
        if (catalog!.Any(item =>
                string.Equals(
                    item.Code,
                    TenantEntitlementCatalogCodes.CompatibilityBaseline,
                    StringComparison.Ordinal)))
        {
            return;
        }

        using var createRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/tenancy/entitlements/catalog")
        {
            Content = JsonContent.Create(new CreateTenantEntitlementCatalogRequest(
                TenantEntitlementCatalogCodes.CompatibilityBaseline,
                "Compatibility Baseline",
                "Backfill integration baseline entitlement.",
                TenantEntitlementTypes.Feature)),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);
    }

    private static async Task<TenantEntitlementBackfillResponse?> PostBackfillAsync(
        HttpClient client,
        string token,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/tenancy/entitlements/backfill")
        {
            Content = JsonContent.Create(new TenantEntitlementBackfillRequest(DryRun: dryRun)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<TenantEntitlementBackfillResponse>(
            cancellationToken);
    }

    private static async Task<TenantSummary> FindHostTenantByIdAsync(
        HttpClient client,
        string token,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/tenants?page=1&pageSize=100");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<TenantSummary>>(cancellationToken);
        Assert.IsNotNull(page);
        return page!.Items.First(item => item.Id == tenantId);
    }
}
