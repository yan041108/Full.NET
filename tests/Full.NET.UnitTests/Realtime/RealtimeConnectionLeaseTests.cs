using System.Security.Claims;
using Full.NET.Realtime;
using Full.NET.Realtime.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Realtime;

/// <summary>权威会话失效与续租拥堵必须由服务端关闭连接。</summary>
[TestClass]
public sealed class RealtimeConnectionLeaseTests
{
    [TestMethod]
    public async Task Revoked_session_aborts_and_expired_lease_cannot_be_revived()
    {
        var authorizer = Substitute.For<IRealtimeConnectionAuthorizer>();
        authorizer.IsAuthorizedAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(true);
        using var services = new ServiceCollection().AddSingleton(authorizer).BuildServiceProvider();
        var time = new ManualTime();
        using var leases = new RealtimeConnectionLeases(services.GetRequiredService<IServiceScopeFactory>(), time);
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("connection");
        context.User.Returns(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())])));
        Assert.IsTrue(await leases.RegisterAsync(context));
        time.Timer!.Expire();
        await leases.RevalidateAsync(CancellationToken.None);
        context.Received(1).Abort();
        Assert.AreEqual(0, time.Timer.Renewals);
        leases.Remove("connection");

        Assert.IsTrue(await leases.RegisterAsync(context));
        authorizer.IsAuthorizedAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(false);
        await leases.RevalidateAsync(CancellationToken.None);
        context.Received(2).Abort();
        Assert.IsFalse(await leases.RegisterAsync(context));
    }

    [TestMethod]
    public void Business_user_groups_are_distinct_across_scopes()
    {
        var user = Guid.NewGuid();
        var groups = new[] { RealtimeGroups.User(user, null), RealtimeGroups.User(user, Guid.NewGuid()),
            RealtimeGroups.User(user, Guid.NewGuid()), RealtimeGroups.AccountControl(user) };
        Assert.AreEqual(4, groups.Distinct().Count());
    }

    private sealed class ManualTime : TimeProvider
    {
        public ManualTimer? Timer { get; private set; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.AreEqual(TimeSpan.FromSeconds(30), dueTime);
            return Timer = new ManualTimer(callback, state);
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        public int Renewals { get; private set; }
        public bool Change(TimeSpan dueTime, TimeSpan period) { Renewals++; return true; }
        public void Expire() => callback(state);
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
