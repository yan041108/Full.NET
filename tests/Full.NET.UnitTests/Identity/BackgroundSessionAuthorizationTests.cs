using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>每个授权阶段只读一次权威会话和权限，阶段之间仍须重新读取。</summary>
[TestClass]
public sealed class BackgroundSessionAuthorizationTests
{
    private static SessionBindingSnapshot Binding() => new(Guid.CreateVersion7(), null, Guid.CreateVersion7(), "stamp", "host", "host");
    [TestMethod]
    public async Task Multiple_permissions_use_one_snapshot_and_next_phase_observes_revocation()
    {
        var binding=Binding(); var sessions=Substitute.For<IBackgroundSessionBindingValidator>();
        var permissions=Substitute.For<IPermissionSnapshotReader>();
        sessions.IsValidAsync(binding,Arg.Any<CancellationToken>()).Returns(true,true,false);
        permissions.ReadAsync(binding.UserId,"host",null,Arg.Any<CancellationToken>())
            .Returns(new PermissionSnapshot(["read","write"],false),new PermissionSnapshot(["read"],false));
        var service=new BackgroundSessionAuthorization(sessions,permissions);
        Assert.IsNotNull(await service.AuthorizeAllAsync(binding,["read","write"]));
        await permissions.Received(1).ReadAsync(binding.UserId,"host",null,Arg.Any<CancellationToken>());
        Assert.IsNull(await service.AuthorizeAllAsync(binding,["read","write"]));
        Assert.IsNull(await service.AuthorizeAllAsync(binding,["read"]));
        await sessions.Received(3).IsValidAsync(binding,Arg.Any<CancellationToken>());
        await permissions.Received(2).ReadAsync(binding.UserId,"host",null,Arg.Any<CancellationToken>());
    }
    [TestMethod]
    public async Task Caller_cannot_remove_permissions_while_session_validation_waits()
    {
        var binding=Binding(); var sessions=Substitute.For<IBackgroundSessionBindingValidator>();
        var permissions=Substitute.For<IPermissionSnapshotReader>();
        var release=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        sessions.IsValidAsync(binding,Arg.Any<CancellationToken>()).Returns(release.Task);
        permissions.ReadAsync(binding.UserId,"host",null,Arg.Any<CancellationToken>()).Returns(new PermissionSnapshot(["read"],false));
        var service=new BackgroundSessionAuthorization(sessions,permissions); var required=new List<string> { "read","write" };
        var pending=service.AuthorizeAllAsync(binding,required); required.Clear(); release.SetResult(true);
        Assert.IsNull(await pending);
    }
    [TestMethod]
    public async Task Super_administrator_cannot_bypass_revoked_session()
    {
        var binding=Binding(); var sessions=Substitute.For<IBackgroundSessionBindingValidator>();
        var permissions=Substitute.For<IPermissionSnapshotReader>();
        sessions.IsValidAsync(binding,Arg.Any<CancellationToken>()).Returns(false);
        permissions.ReadAsync(binding.UserId,"host",null,Arg.Any<CancellationToken>()).Returns(new PermissionSnapshot([],true));
        Assert.IsNull(await new BackgroundSessionAuthorization(sessions,permissions).AuthorizeAllAsync(binding,["read"]));
        await permissions.DidNotReceiveWithAnyArgs().ReadAsync(default,default!,default,default);
    }
}
