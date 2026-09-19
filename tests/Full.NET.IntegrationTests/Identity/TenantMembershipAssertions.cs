using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class TenantMembershipAssertions
{
    public static async Task VerifyAsync(
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
            ["tenancy.tenant_quota.read"],
            cancellationToken);
        var usedBefore = await ReadIdentitySeatsUsedAsync(
            hostClient,
            quotaToken,
            acmeTenant.Id,
            cancellationToken);

        var inviteToken = await factory.CreateHostAccessTokenAsync(
            [
                "identity.tenant_members.read",
                "identity.tenant_members.invite",
                "tenancy.tenants.switch",
            ],
            cancellationToken);
        var inviteTenantToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(
            hostClient,
            inviteToken,
            acmeTenant.Id,
            cancellationToken);

        var inviteEmail = $"member-{Guid.NewGuid():N}@example.com";
        using var inviteRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/identity/tenant-members/invitations")
        {
            Content = JsonContent.Create(new CreateTenantInvitationRequest(
                inviteEmail,
                TenantMemberRoles.Member)),
        };
        inviteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", inviteTenantToken);
        using var inviteResponse = await tenantClient.SendAsync(inviteRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, inviteResponse.StatusCode);
        var invitation = await inviteResponse.Content.ReadFromJsonAsync<CreateTenantInvitationResult>(
            cancellationToken);
        Assert.IsNotNull(invitation);
        Assert.IsFalse(string.IsNullOrWhiteSpace(invitation!.InvitationToken));
        Assert.AreEqual(inviteEmail, invitation.Invitation.TargetEmail);

        var inviteeUsername = $"invitee-{Guid.NewGuid():N}";
        var invitee = await factory.CreateHostIdentityAsync(
            inviteeUsername,
            ["tenancy.tenants.switch"],
            cancellationToken);
        await factory.EnsureHostUserProfileEmailAsync(
            invitee.UserId,
            inviteEmail,
            "Invitee User",
            cancellationToken);
        var inviteeHostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            hostClient,
            inviteeUsername,
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var inviteeTenantToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(
            hostClient,
            inviteeHostToken,
            acmeTenant.Id,
            cancellationToken);

        using var acceptRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/identity/tenant-invitations/accept")
        {
            Content = JsonContent.Create(new AcceptTenantInvitationRequest(invitation.InvitationToken)),
        };
        acceptRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", inviteeTenantToken);
        using var acceptResponse = await tenantClient.SendAsync(acceptRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, acceptResponse.StatusCode);
        var accepted = await acceptResponse.Content.ReadFromJsonAsync<AcceptTenantInvitationResponse>(
            cancellationToken);
        Assert.IsNotNull(accepted);
        Assert.AreEqual(acmeTenant.Id, accepted!.TenantId);
        Assert.AreEqual(invitee.UserId, accepted.UserId);
        Assert.AreEqual(TenantMemberStatuses.Active, accepted.Status);

        var usedAfter = await ReadIdentitySeatsUsedAsync(
            hostClient,
            quotaToken,
            acmeTenant.Id,
            cancellationToken);
        Assert.AreEqual(usedBefore + 1, usedAfter);
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
