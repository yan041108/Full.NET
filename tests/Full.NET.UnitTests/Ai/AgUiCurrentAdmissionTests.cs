using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.AgUi;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>持续读取复用Identity权威交互契约，不能借新会话或旧权限继续流式交付。</summary>
[TestClass]
public sealed class AgUiCurrentAdmissionTests
{
    [TestMethod]
    [DataRow("permission")][DataRow("session")][DataRow("user")][DataRow("tenant")]
    public async Task Current_admission_change_rejects_the_original_stream(string changed)
    {
        var tenant=Substitute.For<ICurrentTenant>();tenant.IsHost.Returns(true);
        var auth=Substitute.For<ICurrentSessionAuthorization>();var actor=new AuthorizedSessionActor(Guid.CreateVersion7(),null,Guid.CreateVersion7());
        AuthorizedSessionActor? current=actor;
        auth.AuthorizeAsync(AiAgentRunPermissions.Read,Arg.Any<CancellationToken>()).Returns(_=>Task.FromResult<AuthorizedSessionActor?>(current));
        var reader=new AiAgentRunAgUiReader(null!,null!,Options.Create(new DatabaseOptions()),tenant,auth,NullLogger<AiAgentRunAgUiReader>.Instance);
        var run=Guid.CreateVersion7();Assert.IsTrue(await reader.CanReadAsync(run,"host",actor.UserId));
        current=changed switch {"permission"=>null,"session"=>actor with {SessionId=Guid.CreateVersion7()},
            "user"=>actor with {UserId=Guid.CreateVersion7()},"tenant"=>actor with {TenantId=Guid.CreateVersion7()},_=>throw new AssertFailedException()};
        Assert.IsFalse(await reader.CanReadAsync(run,"host",actor.UserId));
    }

    [TestMethod]
    public async Task Scope_change_during_authority_read_and_unavailable_authority_fail_closed()
    {
        var tenant=Substitute.For<ICurrentTenant>();tenant.IsHost.Returns(true);
        var auth=Substitute.For<ICurrentSessionAuthorization>();var actor=new AuthorizedSessionActor(Guid.CreateVersion7(),null,Guid.CreateVersion7());
        var reader=new AiAgentRunAgUiReader(null!,null!,Options.Create(new DatabaseOptions()),tenant,auth,NullLogger<AiAgentRunAgUiReader>.Instance);
        auth.AuthorizeAsync(AiAgentRunPermissions.Read,Arg.Any<CancellationToken>()).Returns(_=>
        {tenant.Id.Returns(Guid.CreateVersion7());return Task.FromResult<AuthorizedSessionActor?>(actor);});
        Assert.IsFalse(await reader.CanReadAsync(Guid.CreateVersion7(),"host",actor.UserId));
        tenant.Id.Returns((Guid?)null);
        auth.AuthorizeAsync(AiAgentRunPermissions.Read,Arg.Any<CancellationToken>()).Returns(_=>Task.FromException<AuthorizedSessionActor?>(new InvalidOperationException("PRIVATE_AUTH_DETAIL")));
        Assert.IsFalse(await reader.CanReadAsync(Guid.CreateVersion7(),"host",actor.UserId));
    }

    [TestMethod]
    public async Task Cancellation_after_authority_wait_never_grants_reading()
    {
        var tenant=Substitute.For<ICurrentTenant>();tenant.IsHost.Returns(true);
        var auth=Substitute.For<ICurrentSessionAuthorization>();var actor=new AuthorizedSessionActor(Guid.CreateVersion7(),null,Guid.CreateVersion7());
        using var cancelled=new CancellationTokenSource();
        auth.AuthorizeAsync(AiAgentRunPermissions.Read,Arg.Any<CancellationToken>()).Returns(_=>{cancelled.Cancel();return Task.FromResult<AuthorizedSessionActor?>(actor);});
        var reader=new AiAgentRunAgUiReader(null!,null!,Options.Create(new DatabaseOptions()),tenant,auth,NullLogger<AiAgentRunAgUiReader>.Instance);
        await Assert.ThrowsAsync<OperationCanceledException>(()=>reader.CanReadAsync(Guid.CreateVersion7(),"host",actor.UserId,cancelled.Token).AsTask());
    }
}
