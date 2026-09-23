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
            cancellationToken,
            password: FullNetApiFactory.TestPassword);
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
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/me/tenant-invitations");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", inviteeHostToken);
        using var listResponse = await hostClient.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var pendingInvitations = await listResponse.Content
            .ReadFromJsonAsync<IReadOnlyList<MyTenantInvitationResponse>>(cancellationToken);
        Assert.IsNotNull(pendingInvitations);
        Assert.IsTrue(pendingInvitations!.Any(item => item.Id == invitation.Invitation.Id));

        using var acceptByIdRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/me/tenant-invitations/{invitation.Invitation.Id:D}/accept");
        acceptByIdRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", inviteeHostToken);
        using var acceptResponse = await hostClient.SendAsync(acceptByIdRequest, cancellationToken);
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

        var provisionToken = await factory.CreateHostAccessTokenAsync(
            [
                "identity.tenant_members.read",
                "identity.tenant_members.provision",
                "tenancy.tenants.switch",
            ],
            cancellationToken);
        var provisionTenantToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(
            hostClient,
            provisionToken,
            acmeTenant.Id,
            cancellationToken);
        var provisionUsername = $"provisioned-{Guid.NewGuid():N}";
        var provisionPassword = FullNetApiFactory.TestPassword;
        using var provisionRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/identity/tenant-members/provision")
        {
            Content = JsonContent.Create(new ProvisionTenantMemberRequest(
                provisionUsername,
                "Provisioned Member",
                provisionPassword,
                TenantMemberRoles.Member,
                null)),
        };
        provisionRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            provisionTenantToken);
        using var provisionResponse = await tenantClient.SendAsync(provisionRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, provisionResponse.StatusCode);
        var provisionedMember = await provisionResponse.Content.ReadFromJsonAsync<TenantMemberResponse>(
            cancellationToken);
        Assert.IsNotNull(provisionedMember);
        Assert.AreEqual(acmeTenant.Id, provisionedMember!.TenantId);
        Assert.AreEqual(provisionUsername, provisionedMember.Username);
        Assert.AreEqual(TenantMemberStatuses.Active, provisionedMember.Status);

        var usedAfterProvision = await ReadIdentitySeatsUsedAsync(
            hostClient,
            quotaToken,
            acmeTenant.Id,
            cancellationToken);
        Assert.AreEqual(usedAfter + 1, usedAfterProvision);

        var provisionedLoginToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            hostClient,
            provisionUsername,
            provisionPassword,
            cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(provisionedLoginToken));
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
