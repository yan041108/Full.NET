using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Features.ManageTenantOwnership;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>Owner 职责不能借 Host 账号管理绕过，退出一家企业不能移走另一家企业的职责。</summary>
[TestClass]
public sealed class TenantOwnerAccountBoundaryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Batch_disable_retire_and_account_type_changes_protect_owner(DatabaseProvider provider)
    {
        using var f = await TenantOwnershipHandoffTests.Fixture.CreateAsync(provider);
        var owner = await f.AddPersonAsync("account-owner", TenantMemberRoles.Owner);
        var ordinary = await f.AddPersonAsync("ordinary"); var admin = await f.HostAdminTokenAsync();
        using (var retired = await f.HostAsync(admin, HttpMethod.Post, $"/api/v1/identity/users/{owner.UserId:D}/retire"))
            await AssertOwnerProtectedAsync(retired);
        var user = (await f.UserAsync(owner.UserId))!;
        using (var changed = await f.HostAsync(admin, HttpMethod.Put, $"/api/v1/identity/users/{owner.UserId:D}",
            new UpdateHostUserRequest("Owner changed type", user.Version, IdentityAccountTypes.SysAdmin))) await AssertOwnerProtectedAsync(changed);
        using (var rename = await f.HostAsync(admin, HttpMethod.Put, $"/api/v1/identity/users/{owner.UserId:D}",
            new UpdateHostUserRequest("Owner may rename", user.Version))) Assert.AreEqual(HttpStatusCode.OK, rename.StatusCode, await rename.Content.ReadAsStringAsync());
        using (var batch = await f.HostAsync(admin, HttpMethod.Post, "/api/v1/identity/users/batch-disable", new { userIds = new[] { owner.UserId, ordinary.UserId } }))
        {
            Assert.AreEqual(HttpStatusCode.OK, batch.StatusCode, await batch.Content.ReadAsStringAsync());
            var result = await batch.Content.ReadFromJsonAsync<BatchHostUserStatusResponse>(); Assert.IsNotNull(result);
            Assert.AreEqual(1, result.SucceededCount);
            var protectedItem = result.Results.Single(item => item.UserId == owner.UserId);
            Assert.IsFalse(protectedItem.Succeeded); Assert.AreEqual(IdentityErrorCodes.TenantOwnerProtected, protectedItem.ErrorCode);
            Assert.IsTrue(result.Results.Single(item => item.UserId == ordinary.UserId).Succeeded);
        }
        var retained = (await f.UserAsync(owner.UserId))!;
        Assert.IsTrue(retained.IsActive); Assert.IsNull(retained.RetiredAtUtc); Assert.AreEqual(IdentityAccountTypes.NormalUser, retained.AccountType);
        Assert.AreEqual("Owner may rename", retained.DisplayName); Assert.IsFalse((await f.UserAsync(ordinary.UserId))!.IsActive);
        Assert.AreEqual(1L, await f.OwnerCountAsync());
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Handoff_and_leave_A_keep_B_owner_and_global_account(DatabaseProvider provider)
    {
        using var f = await TenantOwnershipHandoffTests.Fixture.CreateAsync(provider);
        var source = await f.AddPersonAsync("multi-owner", TenantMemberRoles.Owner); var target = await f.AddPersonAsync("new-owner");
        var domain = "owner-b-" + Guid.NewGuid().ToString("N") + ".localhost"; TenantSummary b;
        await using (var setup = f.Factory.Services.CreateAsyncScope())
        {
            var current = setup.ServiceProvider.GetRequiredService<CurrentTenantAccessor>(); current.SetHost();
            var created = await setup.ServiceProvider.GetRequiredService<ITenantProvisioningService>().ProvisionAsync(
                new ProvisionTenantRequest("owner-b-" + Guid.NewGuid().ToString("N"), "Other owner responsibility", domain));
            Assert.IsTrue(created.IsSuccess, created.Error?.Code); b = created.Value!; current.SetTenant(new TenantContext(b.Id, b.Identifier, b.Name));
            await setup.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(TenantMembershipSql.InsertMember,
                IdentitySqlParameters.Create(("Id", Guid.CreateVersion7()), ("UserId", source.UserId), ("MemberRole", TenantMemberRoles.Owner),
                    ("Status", TenantMemberStatuses.Active), ("CreatedAtUtc", DateTimeOffset.UtcNow), ("UpdatedAtUtc", DateTimeOffset.UtcNow), ("Version", 1)));
        }
        using var bClient = f.Factory.CreateClientForHost(domain);
        var bHost = await IntegrationTestAuthHelper.LoginAsHostUserAsync(f.Host, source.Username, FullNetApiFactory.TestPassword);
        var bToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(f.Host, bHost, b.Id);
        bClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bToken);
        var before = await bClient.GetFromJsonAsync<TenantOwnershipStateResponse>("/api/v1/identity/tenant-members/ownership");
        Assert.IsNotNull(before); Assert.AreEqual(source.UserId, before.Owner!.UserId); Assert.AreEqual(1, before.Owner.Version);
        var aToken = await f.TenantTokenAsync(source);
        await f.SuccessAsync(aToken, new TransferTenantOwnerRequest(Guid.CreateVersion7(), source.MemberId, 1, target.MemberId, 1, FullNetApiFactory.TestPassword, null));
        using (var left = await f.SendAsync(aToken, HttpMethod.Post, "/api/v1/identity/tenant-members/me/leave", new LeaveTenantMembershipRequest(2)))
            Assert.AreEqual(HttpStatusCode.OK, left.StatusCode);
        var after = await bClient.GetFromJsonAsync<TenantOwnershipStateResponse>("/api/v1/identity/tenant-members/ownership");
        Assert.IsNotNull(after); Assert.AreEqual(before.Owner, after.Owner); Assert.IsTrue(after.CanTransfer);
        var admin = await f.HostAdminTokenAsync();
        using (var disabled = await f.HostAsync(admin, HttpMethod.Post, $"/api/v1/identity/users/{source.UserId:D}/disable")) await AssertOwnerProtectedAsync(disabled);
        Assert.IsTrue((await f.UserAsync(source.UserId))!.IsActive);
        var stillValid = await bClient.GetFromJsonAsync<TenantOwnershipStateResponse>("/api/v1/identity/tenant-members/ownership");
        Assert.AreEqual(before.Owner, stillValid!.Owner);
    }

    private static async Task AssertOwnerProtectedAsync(HttpResponseMessage response)
    {
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, await response.Content.ReadAsStringAsync());
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(IdentityErrorCodes.TenantOwnerProtected, problem.RootElement.GetProperty("code").GetString());
    }
}
