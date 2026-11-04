using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>邀请详情隔离、撤销幂等及接受/撤销竞争的真实双库 HTTP 回归。</summary>
[TestClass]
public sealed class TenantInvitationRevocationTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Pending_revocation_preserves_terminal_facts_and_tenant_isolation(DatabaseProvider provider)
    {
        using var factory = new FullNetApiFactory(provider, await IdentityBrowserTestSupport.DatabaseAsync(provider));
        await factory.InitializeAsync();
        using var host = factory.CreateClientForHost("localhost");
        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenantClient);
        var hostToken = await factory.CreateHostAccessTokenAsync([IdentityTenantMembershipPermissions.Read,
            IdentityTenantMembershipPermissions.Invite, IdentityTenantMembershipPermissions.RevokeInvitation, "tenancy.tenants.switch"]);
        var token = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, hostToken, tenant.Id);
        var email = "revoke-" + Guid.NewGuid().ToString("N") + "@example.test";
        var invitation = await InviteAsync(tenantClient, token, email);
        var original = await ReadAsync(tenantClient, token, invitation.Invitation.Id);
        Assert.AreEqual(tenant.Id, original.TenantId); Assert.AreEqual(email, original.TargetEmail);
        Assert.AreEqual(TenantInvitationStatuses.Pending, original.Status);
        Assert.AreEqual(1, original.Version);

        var noRead = await factory.CreateHostAccessTokenAsync([IdentityTenantMembershipPermissions.RevokeInvitation, "tenancy.tenants.switch"]);
        var noReadTenant = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, noRead, tenant.Id);
        using (var denied = await SendAsync(tenantClient, noReadTenant, HttpMethod.Get, DetailPath(original.Id)))
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        var suffix = Guid.NewGuid().ToString("N");
        TenantSummary other;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var provisioned = await scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>()
                .ProvisionAsync(new ProvisionTenantRequest("other-" + suffix, "Other organization", "other-" + suffix + ".localhost"));
            Assert.IsTrue(provisioned.IsSuccess, provisioned.Error?.Code); other = provisioned.Value!;
        }
        using var otherClient = factory.CreateClientForHost("other-" + suffix + ".localhost");
        // 切换已轮换原 Host 凭据；另一账号读取另一租户，不能借旧令牌或使原管理会话失效。
        var otherHostToken = await factory.CreateHostAccessTokenAsync([IdentityTenantMembershipPermissions.Read,
            IdentityTenantMembershipPermissions.RevokeInvitation, "tenancy.tenants.switch"]);
        var otherToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, otherHostToken, other.Id);
        using (var hidden = await SendAsync(otherClient, otherToken, HttpMethod.Get, DetailPath(original.Id)))
            Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        using (var hidden = await SendAsync(otherClient, otherToken, HttpMethod.Post, DetailPath(original.Id) + "/revoke"))
            Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);

        using var revoked = await SendAsync(tenantClient, token, HttpMethod.Post, DetailPath(original.Id) + "/revoke");
        Assert.AreEqual(HttpStatusCode.OK, revoked.StatusCode);
        var receipt = await revoked.Content.ReadFromJsonAsync<TenantInvitationResponse>(); Assert.IsNotNull(receipt);
        Assert.AreEqual(TenantInvitationStatuses.Revoked, receipt.Status); Assert.AreEqual(2, receipt.Version);
        using var repeated = await SendAsync(tenantClient, token, HttpMethod.Post, DetailPath(original.Id) + "/revoke");
        Assert.AreEqual(HttpStatusCode.OK, repeated.StatusCode);
        var replay = await repeated.Content.ReadFromJsonAsync<TenantInvitationResponse>(); Assert.IsNotNull(replay);
        Assert.AreEqual(receipt, replay);

        var user = await factory.CreateHostIdentityAsync("revocation-user-" + suffix, [], password: FullNetApiFactory.TestPassword);
        await factory.EnsureHostUserProfileEmailAsync(user.UserId, email, "Revocation fixture member");
        var userToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, user.Username, FullNetApiFactory.TestPassword);
        using (var rejected = await SendAsync(host, userToken, HttpMethod.Post, "/api/v1/identity/tenant-invitations/accept",
            new AcceptTenantInvitationRequest(invitation.InvitationToken))) Assert.AreEqual(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);

        var acceptedInvitation = await InviteAsync(tenantClient, token, email);
        using var accepted = await SendAsync(host, userToken, HttpMethod.Post, "/api/v1/identity/tenant-invitations/accept",
            new AcceptTenantInvitationRequest(acceptedInvitation.InvitationToken));
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
        var member = await accepted.Content.ReadFromJsonAsync<AcceptTenantInvitationResponse>(); Assert.IsNotNull(member);
        Assert.AreEqual(TenantMemberStatuses.Active, member.Status);
        var acceptedBefore = await ReadAsync(tenantClient, token, acceptedInvitation.Invitation.Id);
        using (var rejected = await SendAsync(tenantClient, token, HttpMethod.Post, DetailPath(acceptedBefore.Id) + "/revoke"))
            Assert.AreEqual(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.AreEqual(acceptedBefore, await ReadAsync(tenantClient, token, acceptedBefore.Id));
        using (var preserved = await SendAsync(tenantClient, token, HttpMethod.Get, "/api/v1/identity/tenant-members/" + member.MemberId.ToString("D")))
        {
            Assert.AreEqual(HttpStatusCode.OK, preserved.StatusCode);
            var current = await preserved.Content.ReadFromJsonAsync<TenantMemberResponse>(); Assert.IsNotNull(current);
            Assert.AreEqual(TenantMemberStatuses.Active, current.Status); Assert.AreEqual(user.UserId, current.UserId);
        }

        // 两个独立 HTTP 请求竞争同一授权，终态只能是已接受或已撤销。
        var racingUser = await factory.CreateHostIdentityAsync("racing-" + suffix, [], password: FullNetApiFactory.TestPassword);
        await factory.EnsureHostUserProfileEmailAsync(racingUser.UserId, "racing-" + suffix + "@example.test", "Racing fixture member");
        var racingToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, racingUser.Username, FullNetApiFactory.TestPassword);
        var racingInvite = await InviteAsync(tenantClient, token, "racing-" + suffix + "@example.test");
        var acceptance = SendAsync(host, racingToken, HttpMethod.Post, "/api/v1/identity/tenant-invitations/accept",
            new AcceptTenantInvitationRequest(racingInvite.InvitationToken));
        var revocation = SendAsync(tenantClient, token, HttpMethod.Post, DetailPath(racingInvite.Invitation.Id) + "/revoke");
        await Task.WhenAll(acceptance, revocation);
        using var acceptedRace = await acceptance; using var revokedRace = await revocation;
        var final = await ReadAsync(tenantClient, token, racingInvite.Invitation.Id);
        Assert.AreEqual(2, final.Version);
        if (final.Status == TenantInvitationStatuses.Accepted)
        {
            Assert.AreEqual(HttpStatusCode.OK, acceptedRace.StatusCode);
            Assert.AreEqual(HttpStatusCode.Conflict, revokedRace.StatusCode);
        }
        else
        {
            Assert.AreEqual(TenantInvitationStatuses.Revoked, final.Status);
            Assert.AreEqual(HttpStatusCode.OK, revokedRace.StatusCode); Assert.IsFalse(acceptedRace.IsSuccessStatusCode);
            using var listed = await SendAsync(tenantClient, token, HttpMethod.Get, "/api/v1/identity/tenant-members?pageSize=100");
            Assert.AreEqual(HttpStatusCode.OK, listed.StatusCode);
            var members = await listed.Content.ReadFromJsonAsync<PagedResult<TenantMemberResponse>>(); Assert.IsNotNull(members);
            Assert.IsFalse(members.Items.Any(item => item.UserId == racingUser.UserId && item.Status == TenantMemberStatuses.Active));
        }
    }

    private static string DetailPath(Guid id) => "/api/v1/identity/tenant-members/invitations/" + id.ToString("D");
    private static async Task<CreateTenantInvitationResult> InviteAsync(HttpClient client, string token, string email)
    {
        using var response = await SendAsync(client, token, HttpMethod.Post, "/api/v1/identity/tenant-members/invitations",
            new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateTenantInvitationResult>())!;
    }
    private static async Task<TenantInvitationResponse> ReadAsync(HttpClient client, string token, Guid id)
    {
        using var response = await SendAsync(client, token, HttpMethod.Get, DetailPath(id));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TenantInvitationResponse>())!;
    }
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Origin", "http://localhost");
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
