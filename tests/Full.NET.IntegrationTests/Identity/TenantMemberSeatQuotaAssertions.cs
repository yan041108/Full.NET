using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class TenantMemberSeatQuotaAssertions
{
    public static async Task VerifyProvisionBlockedWhenSeatsAtCapacityAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var hostClient = factory.CreateClientForHost("localhost");
        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        var acmeTenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(
            tenantClient,
            cancellationToken);

        var quotaToken = await factory.CreateHostAccessTokenAsync(
            [
                "tenancy.tenant_quota.read",
                "tenancy.tenant_quota.manage",
            ],
            cancellationToken);
        var used = await ReadIdentitySeatsUsedAsync(
            hostClient,
            quotaToken,
            acmeTenant.Id,
            cancellationToken);
        await UpsertIdentitySeatLimitAsync(
            hostClient,
            quotaToken,
            acmeTenant.Id,
            used,
            cancellationToken);

        var provisionToken = await factory.CreateHostAccessTokenAsync(
            [
                "identity.tenant_members.provision",
                "tenancy.tenants.switch",
            ],
            cancellationToken);
        var tenantToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(
            hostClient,
            provisionToken,
            acmeTenant.Id,
            cancellationToken);

        using var provisionRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/identity/tenant-members/provision")
        {
            Content = JsonContent.Create(new ProvisionTenantMemberRequest(
                $"quota-{Guid.NewGuid():N}"[..16],
                "Quota Blocked",
                FullNetApiFactory.TestPassword,
                TenantMemberRoles.Member,
                null)),
        };
        provisionRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            tenantToken);
        using var provisionResponse = await tenantClient.SendAsync(provisionRequest, cancellationToken);
        Assert.IsFalse(provisionResponse.IsSuccessStatusCode);
        using var problem = JsonDocument.Parse(
            await provisionResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            TenancyErrorCodes.QuotaExceeded,
            problem.RootElement.GetProperty("code").GetString());
    }

    public static async Task VerifyAcceptInvitationBlockedWhenSeatsAtCapacityAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var hostClient = factory.CreateClientForHost("localhost");
        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        var acmeTenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(
            tenantClient,
            cancellationToken);

        var quotaToken = await factory.CreateHostAccessTokenAsync(
            [
                "tenancy.tenant_quota.read",
                "tenancy.tenant_quota.manage",
            ],
            cancellationToken);
        var used = await ReadIdentitySeatsUsedAsync(
            hostClient,
            quotaToken,
            acmeTenant.Id,
            cancellationToken);
        await UpsertIdentitySeatLimitAsync(
            hostClient,
            quotaToken,
            acmeTenant.Id,
            used,
            cancellationToken);

        var inviteEmail = $"quota-{Guid.NewGuid():N}@example.com";
        var inviteToken = await factory.CreateHostAccessTokenAsync(
            [
                "identity.tenant_members.invite",
                "tenancy.tenants.switch",
            ],
            cancellationToken);
        var inviteTenantToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(
            hostClient,
            inviteToken,
            acmeTenant.Id,
            cancellationToken);
        using var inviteRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/identity/tenant-members/invitations")
        {
            Content = JsonContent.Create(new CreateTenantInvitationRequest(
                inviteEmail,
                TenantMemberRoles.Member)),
        };
        inviteRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            inviteTenantToken);
        using var inviteResponse = await tenantClient.SendAsync(inviteRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, inviteResponse.StatusCode);
        var invitation = await inviteResponse.Content.ReadFromJsonAsync<CreateTenantInvitationResult>(
            cancellationToken);
        Assert.IsNotNull(invitation);

        var inviteeUsername = $"quota-invitee-{Guid.NewGuid():N}";
        var invitee = await factory.CreateHostIdentityAsync(
            inviteeUsername,
            ["tenancy.tenants.switch"],
            cancellationToken,
            password: FullNetApiFactory.TestPassword);
        await factory.EnsureHostUserProfileEmailAsync(
            invitee.UserId,
            inviteEmail,
            "Quota Invitee",
            cancellationToken);
        var inviteeHostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            hostClient,
            inviteeUsername,
            FullNetApiFactory.TestPassword,
            cancellationToken);
        using var acceptRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/me/tenant-invitations/{invitation.Invitation.Id:D}/accept");
        acceptRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", inviteeHostToken);
        using var acceptResponse = await hostClient.SendAsync(acceptRequest, cancellationToken);
        Assert.IsFalse(acceptResponse.IsSuccessStatusCode);
        using var problem = JsonDocument.Parse(
            await acceptResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            TenancyErrorCodes.QuotaExceeded,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task UpsertIdentitySeatLimitAsync(
        HttpClient hostClient,
        string accessToken,
        Guid tenantId,
        long limit,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/tenancy/tenants/{tenantId:D}/quota/metrics")
        {
            Content = JsonContent.Create(new UpsertTenantQuotaMetricRequest(
                TenantQuotaMetricCodes.IdentitySeats,
                TenantQuotaDefaults.PeriodKey,
                limit)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await hostClient.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<long> ReadIdentitySeatsUsedAsync(
        HttpClient hostClient,
        string accessToken,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/tenancy/tenants/{tenantId:D}/quota/metrics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await hostClient.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var metrics = await response.Content.ReadFromJsonAsync<IReadOnlyList<TenantQuotaMetricResponse>>(
            cancellationToken);
        Assert.IsNotNull(metrics);
        var metric = metrics!.FirstOrDefault(item =>
            item.MetricCode == TenantQuotaMetricCodes.IdentitySeats
            && item.PeriodKey == TenantQuotaDefaults.PeriodKey);
        Assert.IsNotNull(metric);
        return metric!.UsedValue;
    }
}
