using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Channels;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Realtime;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Realtime;

/// <summary>使用真实 Kestrel/WebSocket 和原凭据验证撤权；不手工触发连接扫描或轮换被测会话。</summary>
[TestClass]
public sealed class TenantMembershipSocketRevocationTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.SqlServer, false)]
    [DataRow(DatabaseProvider.MySql, true)]
    [DataRow(DatabaseProvider.MySql, false)]
    public async Task Leaving_or_removing_A_closes_original_socket_and_preserves_B(DatabaseProvider provider, bool leaveSelf)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var token = deadline.Token;
        var database = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, database, new Dictionary<string, string?> {
            ["Identity:SessionLoginPolicy"] = "AllowMultiple" });
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0)); await factory.InitializeAsync(token);
        var address = new Uri(factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        using var host = Client(address, "localhost"); using var aClient = Client(address, "acme.localhost");
        var a = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(aClient, token);
        var identity = await factory.CreateHostIdentityAsync("socket-member-" + Guid.CreateVersion7().ToString("N"),
            [IdentityTenantMembershipPermissions.Read, IdentityTenantMembershipPermissions.LeaveSelf], token, FullNetApiFactory.TestPassword);
        var bDomain = "socket-b-" + Guid.CreateVersion7().ToString("N") + ".localhost";
        TenantSummary b; var aMember = Guid.CreateVersion7();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var current = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>(); current.SetHost();
            var created = await scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>().ProvisionAsync(
                new ProvisionTenantRequest("socket-b-" + Guid.CreateVersion7().ToString("N"), "Socket B", bDomain), token);
            Assert.IsTrue(created.IsSuccess, created.Error?.Code); b = created.Value!;
            foreach (var entry in new[] { (a.Id, "acme", "Acme", aMember), (b.Id, b.Identifier, b.Name, Guid.CreateVersion7()) })
            {
                current.SetTenant(new TenantContext(entry.Item1, entry.Item2, entry.Item3));
                await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(TenantMembershipSql.InsertMember,
                    IdentitySqlParameters.Create(("Id", entry.Item4), ("UserId", identity.UserId), ("MemberRole", TenantMemberRoles.Member),
                        ("Status", TenantMemberStatuses.Active), ("CreatedAtUtc", DateTimeOffset.UtcNow), ("UpdatedAtUtc", DateTimeOffset.UtcNow), ("Version", 1)), token);
            }
        }
        var aToken = await LoginTenantAsync(host, identity.Username, a.Id, token);
        var bToken = await LoginTenantAsync(host, identity.Username, b.Id, token);
        await using var aSocket = new Subscriber(address, "acme.localhost", aToken);
        await using var bSocket = new Subscriber(address, bDomain, bToken);
        await aSocket.Connection.StartAsync(token); await bSocket.Connection.StartAsync(token);
        await ReceiveAsync(factory, aSocket, identity.UserId, new(a.Id, "acme", "Acme"), token);
        await ReceiveAsync(factory, bSocket, identity.UserId, new(b.Id, b.Identifier, b.Name), token);
        if (leaveSelf)
        {
            aClient.DefaultRequestHeaders.Authorization = new("Bearer", aToken);
            using var left = await aClient.PostAsJsonAsync("/api/v1/identity/tenant-members/me/leave", new LeaveTenantMembershipRequest(1), token);
            Assert.AreEqual(HttpStatusCode.OK, left.StatusCode, await left.Content.ReadAsStringAsync(token));
        }
        else
        {
            aClient.DefaultRequestHeaders.Authorization = new("Bearer", await LoginTenantAsync(host, "admin", a.Id, token));
            using var removed = await aClient.DeleteAsync($"/api/v1/identity/tenant-members/{aMember:D}?version=1", token);
            Assert.AreEqual(HttpStatusCode.OK, removed.StatusCode, await removed.Content.ReadAsStringAsync(token));
        }
        // 实际 HostedService 定时核证并关闭原网络连接；保留原 A/B 令牌，不通过返回 Host 触发轮换。
        await aSocket.Closed.Task.WaitAsync(TimeSpan.FromSeconds(35), token);
        Assert.AreEqual(HubConnectionState.Disconnected, aSocket.Connection.State);
        await using var rejected = new Subscriber(address, "acme.localhost", aToken);
        try { await rejected.Connection.StartAsync(token); }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException or System.Net.WebSockets.WebSocketException) { }
        if (rejected.Connection.State != HubConnectionState.Disconnected)
            await rejected.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.AreEqual(HubConnectionState.Disconnected, rejected.Connection.State);
        await ReceiveAsync(factory, bSocket, identity.UserId, new(b.Id, b.Identifier, b.Name), token);
        Assert.AreEqual(HubConnectionState.Connected, bSocket.Connection.State);
        Assert.IsFalse(bSocket.Closed.Task.IsCompleted);
        using var bClient = Client(address, bDomain); bClient.DefaultRequestHeaders.Authorization = new("Bearer", bToken);
        using var bMembers = await bClient.GetAsync("/api/v1/identity/tenant-members?page=1&pageSize=20", token);
        Assert.AreEqual(HttpStatusCode.OK, bMembers.StatusCode);
    }

    private static HttpClient Client(Uri address, string domain)
    {
        var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = address };
        client.DefaultRequestHeaders.Host = domain; client.DefaultRequestHeaders.Add("Origin", "http://localhost"); return client;
    }
    private static async Task<string> LoginTenantAsync(HttpClient host, string username, Guid tenant, CancellationToken token) =>
        await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host,
            await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, username, FullNetApiFactory.TestPassword, token), tenant, token);
    private static async Task ReceiveAsync(FullNetApiFactory factory, Subscriber subscriber, Guid user, TenantContext tenant, CancellationToken token)
    {
        await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetTenant(tenant);
        var publisher = scope.ServiceProvider.GetRequiredService<IRealtimePublisher>(); var code = "test.membership.socket." + Guid.CreateVersion7().ToString("N");
        // 握手完成早于组注册；在有界窗口内用实际消息证明连接已加入正确企业组。
        using var ready = CancellationTokenSource.CreateLinkedTokenSource(token); ready.CancelAfter(TimeSpan.FromSeconds(5));
        while (true)
        {
            await publisher.PublishToUserAsync(user, new RealtimeMessage(code), ready.Token);
            while (subscriber.Messages.Reader.TryRead(out var received)) if (received.Code == code) return;
            await Task.Delay(50, ready.Token);
        }
    }
    private sealed class Subscriber : IAsyncDisposable
    {
        internal HubConnection Connection { get; }
        internal TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Channel<RealtimeMessage> Messages { get; } = Channel.CreateBounded<RealtimeMessage>(new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.DropOldest });
        private readonly IDisposable subscription;
        internal Subscriber(Uri address, string domain, string token)
        {
            Connection = new HubConnectionBuilder().WithUrl(new Uri(address, "/hubs/notifications"), options => {
                options.AccessTokenProvider = () => Task.FromResult<string?>(token); options.Headers["Host"] = domain;
                options.Headers["Origin"] = "http://localhost"; options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true; options.Proxy = new WebProxy();
            }).Build();
            Connection.Closed += _ => { Closed.TrySetResult(); return Task.CompletedTask; };
            subscription = Connection.On<RealtimeMessage>("ReceiveMessageAsync", message => Messages.Writer.TryWrite(message));
        }
        public async ValueTask DisposeAsync() { subscription.Dispose(); await Connection.DisposeAsync(); }
    }
}
