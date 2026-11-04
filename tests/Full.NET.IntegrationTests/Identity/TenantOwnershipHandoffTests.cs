using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Features.ManageTenantOwnership;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>真实 HTTP 和本地事务核对首位 Owner、交接回执、账号保护与同企业并发；目录元数据不参与授权。</summary>
[TestClass]
public sealed class TenantOwnershipHandoffTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Bootstrap_handoff_receipts_and_owner_account_protection(DatabaseProvider provider)
    {
        using var f = await Fixture.CreateAsync(provider);
        var source = await f.AddPersonAsync("source"); var target = await f.AddPersonAsync("target");
        var platform = await f.PlatformTenantTokenAsync();
        var initial = await f.StateAsync(platform); Assert.IsNull(initial.Owner); Assert.IsTrue(initial.CanInitialize);
        var initialize = new TransferTenantOwnerRequest(Guid.CreateVersion7(), null, null, source.MemberId, 1, FullNetApiFactory.TestPassword, null);
        using (var wrong = await f.TransferAsync(platform, initialize with { OperationId = Guid.CreateVersion7(), CurrentPassword = "wrong-password" }))
            Assert.AreEqual(HttpStatusCode.Unauthorized, wrong.StatusCode);
        var first = await f.SuccessAsync(platform, initialize);
        Assert.IsNull(first.SourceMemberId); Assert.AreEqual(source.UserId, first.TargetUserId); Assert.AreEqual(2, first.TargetVersion);
        // 回读已提交事实不重复消费验证凭据；不同目标不能复用原操作。
        Assert.AreEqual(first, await f.SuccessAsync(platform, initialize with { CurrentPassword = "" }));
        using (var mismatch = await f.TransferAsync(platform, initialize with { TargetMemberId = target.MemberId }))
            Assert.AreEqual(HttpStatusCode.Conflict, mismatch.StatusCode);
        using (var bootstrapAgain = await f.TransferAsync(platform, initialize with { OperationId = Guid.CreateVersion7(), TargetMemberId = target.MemberId }))
            Assert.AreEqual(HttpStatusCode.Conflict, bootstrapAgain.StatusCode);
        var established = await f.StateAsync(platform);
        Assert.IsFalse(established.CanInitialize); Assert.IsFalse(established.CanTransfer);
        Assert.AreEqual(source.MemberId, established.Owner!.Id);
        var hostAdmin = await f.HostAdminTokenAsync();
        using (var protectedAccount = await f.HostAsync(hostAdmin, HttpMethod.Post, $"/api/v1/identity/users/{source.UserId:D}/disable"))
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, protectedAccount.StatusCode);
        Assert.IsTrue((await f.UserAsync(source.UserId))!.IsActive);
        var sourceToken = await f.TenantTokenAsync(source);
        using (var protectedLeave = await f.SendAsync(sourceToken, HttpMethod.Post, "/api/v1/identity/tenant-members/me/leave", new LeaveTenantMembershipRequest(2)))
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, protectedLeave.StatusCode);
        var change = new TransferTenantOwnerRequest(Guid.CreateVersion7(), source.MemberId, 2, target.MemberId, 1, FullNetApiFactory.TestPassword, null);
        using (var wrong = await f.TransferAsync(sourceToken, change with { OperationId = Guid.CreateVersion7(), CurrentPassword = "wrong-password" }))
            Assert.AreEqual(HttpStatusCode.Unauthorized, wrong.StatusCode);
        var handed = await f.SuccessAsync(sourceToken, change);
        Assert.AreEqual(source.UserId, handed.SourceUserId); Assert.AreEqual(2, handed.SourcePreviousVersion); Assert.AreEqual(3, handed.SourceVersion);
        Assert.AreEqual(target.UserId, handed.TargetUserId); Assert.AreEqual(1, handed.TargetPreviousVersion); Assert.AreEqual(2, handed.TargetVersion);
        Assert.AreEqual(handed, await f.SuccessAsync(sourceToken, change with { CurrentPassword = "" }));
        using (var mismatched = await f.TransferAsync(sourceToken, change with { TargetVersion = 2 })) Assert.AreEqual(HttpStatusCode.Conflict, mismatched.StatusCode);
        using (var stale = await f.TransferAsync(sourceToken, change with { OperationId = Guid.CreateVersion7() })) Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.AreEqual(handed, await f.ReceiptAsync(sourceToken, change.OperationId));
        var current = await f.StateAsync(sourceToken); Assert.AreEqual(target.MemberId, current.Owner!.Id); Assert.IsFalse(current.CanTransfer);
        var former = await f.MemberAsync(sourceToken, source.MemberId); Assert.AreEqual(TenantMemberRoles.Admin, former.MemberRole); Assert.AreEqual(3, former.Version);
        var recipient = await f.MemberAsync(sourceToken, target.MemberId); Assert.AreEqual(TenantMemberRoles.Owner, recipient.MemberRole); Assert.AreEqual(2, recipient.Version);
        var targetToken = await f.TenantTokenAsync(target);
        using (var privateReceipt = await f.SendAsync(targetToken, HttpMethod.Get, $"/api/v1/identity/tenant-members/ownership/operations/{change.OperationId:D}"))
            Assert.AreEqual(HttpStatusCode.NotFound, privateReceipt.StatusCode);
        using (var protectedAccount = await f.HostAsync(hostAdmin, HttpMethod.Post, $"/api/v1/identity/users/{target.UserId:D}/disable"))
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, protectedAccount.StatusCode);
        using (var protectedMember = await f.SendAsync(platform, HttpMethod.Delete, $"/api/v1/identity/tenant-members/{target.MemberId:D}?version=2"))
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, protectedMember.StatusCode);
        using (var left = await f.SendAsync(sourceToken, HttpMethod.Post, "/api/v1/identity/tenant-members/me/leave", new LeaveTenantMembershipRequest(3)))
            Assert.AreEqual(HttpStatusCode.OK, left.StatusCode);
        using (var nowAllowed = await f.HostAsync(hostAdmin, HttpMethod.Post, $"/api/v1/identity/users/{source.UserId:D}/disable"))
            Assert.AreEqual(HttpStatusCode.OK, nowAllowed.StatusCode);
        Assert.IsFalse((await f.UserAsync(source.UserId))!.IsActive); Assert.IsTrue((await f.UserAsync(target.UserId))!.IsActive);
        Assert.AreEqual(target.MemberId, (await f.StateAsync(targetToken)).Owner!.Id);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Concurrent_handoffs_and_target_removal_preserve_one_owner(DatabaseProvider provider)
    {
        using var f = await Fixture.CreateAsync(provider);
        // 只设置并发用例的初始职责；被测请求仍经过真实登录、强认证、锁和事务。
        var source = await f.AddPersonAsync("source", TenantMemberRoles.Owner);
        var b = await f.AddPersonAsync("b"); var c = await f.AddPersonAsync("c");
        var token = await f.TenantTokenAsync(source);
        var one = new TransferTenantOwnerRequest(Guid.CreateVersion7(), source.MemberId, 1, b.MemberId, 1, FullNetApiFactory.TestPassword, null);
        var two = one with { OperationId = Guid.CreateVersion7(), TargetMemberId = c.MemberId };
        var responses = await Task.WhenAll(f.TransferAsync(token, one), f.TransferAsync(token, two));
        Person winner, loser;
        try
        {
            Assert.AreEqual(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.AreEqual(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            winner = responses[0].IsSuccessStatusCode ? b : c; loser = winner == b ? c : b;
        }
        finally { foreach (var response in responses) response.Dispose(); }
        Assert.AreEqual(winner.MemberId, (await f.StateAsync(token)).Owner!.Id);
        Assert.AreEqual(2, (await f.MemberAsync(token, source.MemberId)).Version);
        var winnerToken = await f.TenantTokenAsync(winner); var platform = await f.PlatformTenantTokenAsync();
        var next = new TransferTenantOwnerRequest(Guid.CreateVersion7(), winner.MemberId, 2, loser.MemberId, 1, FullNetApiFactory.TestPassword, null);
        var competing = await Task.WhenAll(f.TransferAsync(winnerToken, next),
            f.SendAsync(platform, HttpMethod.Delete, $"/api/v1/identity/tenant-members/{loser.MemberId:D}?version=1"));
        try
        {
            if (competing[0].IsSuccessStatusCode)
            {
                Assert.IsTrue(competing[1].StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity);
                Assert.AreEqual(loser.MemberId, (await f.StateAsync(platform)).Owner!.Id);
            }
            else
            {
                Assert.AreEqual(HttpStatusCode.Conflict, competing[0].StatusCode);
                Assert.AreEqual(HttpStatusCode.OK, competing[1].StatusCode);
                Assert.AreEqual(winner.MemberId, (await f.StateAsync(platform)).Owner!.Id);
            }
        }
        finally { foreach (var response in competing) response.Dispose(); }
        Assert.AreEqual(1L, await f.OwnerCountAsync());
    }

    internal sealed record Person(Guid UserId, string Username, Guid MemberId);
    internal sealed class Fixture : IDisposable
    {
        internal readonly FullNetApiFactory Factory;
        internal readonly HttpClient Host, Tenant;
        internal readonly TenantContext Context;
        private Fixture(FullNetApiFactory factory, HttpClient host, HttpClient tenant, TenantContext context)
        { Factory = factory; Host = host; Tenant = tenant; Context = context; }
        internal static async Task<Fixture> CreateAsync(DatabaseProvider provider, Action<IServiceCollection>? configure = null,
            IReadOnlyDictionary<string, string?>? extraSettings = null)
        {
            var settings = new Dictionary<string, string?> { ["Identity:SessionLoginPolicy"] = "AllowMultiple" };
            if (extraSettings is not null) foreach (var entry in extraSettings) settings[entry.Key] = entry.Value;
            var factory = new FullNetApiFactory(provider, await IdentityBrowserTestSupport.DatabaseAsync(provider), settings, configureTestServices: configure);
            await factory.InitializeAsync(); var host = factory.CreateClientForHost("localhost"); var tenant = factory.CreateClientForHost("acme.localhost");
            var current = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenant);
            return new(factory, host, tenant, new TenantContext(current.Id, "acme", "Acme"));
        }
        internal async Task<Person> AddPersonAsync(string label, string role = TenantMemberRoles.Member)
        {
            var account = await Factory.CreateHostIdentityAsync("ownership-" + label + "-" + Guid.NewGuid().ToString("N"),
                [IdentityTenantMembershipPermissions.Read, IdentityTenantMembershipPermissions.TransferOwnership, IdentityTenantMembershipPermissions.LeaveSelf], password: FullNetApiFactory.TestPassword);
            var member = Guid.CreateVersion7();
            await using var scope = Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetTenant(Context);
            await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(TenantMembershipSql.InsertMember,
                IdentitySqlParameters.Create(("Id", member), ("UserId", account.UserId), ("MemberRole", role), ("Status", TenantMemberStatuses.Active),
                    ("CreatedAtUtc", DateTimeOffset.UtcNow), ("UpdatedAtUtc", DateTimeOffset.UtcNow), ("Version", 1)));
            return new(account.UserId, account.Username, member);
        }
        internal Task<string> HostAdminTokenAsync() => IntegrationTestAuthHelper.LoginAsHostUserAsync(Host, "admin", FullNetApiFactory.TestPassword);
        internal async Task<string> PlatformTenantTokenAsync() => await IntegrationTestTenantContextHelper.SwitchToTenantAsync(Host, await HostAdminTokenAsync(), Context.Id);
        internal async Task<string> TenantTokenAsync(Person person) => await IntegrationTestTenantContextHelper.SwitchToTenantAsync(Host,
            await IntegrationTestAuthHelper.LoginAsHostUserAsync(Host, person.Username, FullNetApiFactory.TestPassword), Context.Id);
        internal Task<HttpResponseMessage> TransferAsync(string token, TransferTenantOwnerRequest request) => SendAsync(token, HttpMethod.Post, "/api/v1/identity/tenant-members/ownership/transfer", request);
        internal async Task<TenantOwnerTransferReceipt> SuccessAsync(string token, TransferTenantOwnerRequest request)
        {
            using var response = await TransferAsync(token, request); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<TenantOwnerTransferReceipt>())!;
        }
        internal async Task<TenantOwnershipStateResponse> StateAsync(string token)
        {
            using var response = await SendAsync(token, HttpMethod.Get, "/api/v1/identity/tenant-members/ownership");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<TenantOwnershipStateResponse>())!;
        }
        internal async Task<TenantOwnerTransferReceipt> ReceiptAsync(string token, Guid operation)
        {
            using var response = await SendAsync(token, HttpMethod.Get, $"/api/v1/identity/tenant-members/ownership/operations/{operation:D}");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode); return (await response.Content.ReadFromJsonAsync<TenantOwnerTransferReceipt>())!;
        }
        internal async Task<TenantMemberResponse> MemberAsync(string token, Guid member)
        {
            using var response = await SendAsync(token, HttpMethod.Get, "/api/v1/identity/tenant-members/" + member.ToString("D"));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode); return (await response.Content.ReadFromJsonAsync<TenantMemberResponse>())!;
        }
        internal async Task<IdentityUserRecord?> UserAsync(Guid user)
        {
            await using var scope = Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            return await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<IdentityUserRecord>(IdentitySql.FindHostUserById, IdentitySqlParameters.Create(("UserId", user)));
        }
        internal async Task<long> OwnerCountAsync()
        {
            await using var scope = Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetTenant(Context);
            return await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<long>(new SqlStatement("test.owner.count", "SELECT COUNT(1) FROM fn_identity_tenant_member WHERE TenantId=@TenantId AND MemberRole='Owner' AND Status='Active'", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId));
        }
        internal Task<HttpResponseMessage> SendAsync(string token, HttpMethod method, string path, object? body = null) => SendAsync(Tenant, token, method, path, body);
        internal Task<HttpResponseMessage> HostAsync(string token, HttpMethod method, string path, object? body = null) => SendAsync(Host, token, method, path, body);
        private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token, HttpMethod method, string path, object? body)
        {
            using var request = new HttpRequestMessage(method, path); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Origin", "http://localhost"); if (body is not null) request.Content = JsonContent.Create(body);
            return await client.SendAsync(request);
        }
        public void Dispose() { Host.Dispose(); Tenant.Dispose(); Factory.Dispose(); }
    }
}
