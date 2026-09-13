using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>后台授权必须重验绑定并读取权限快照。</summary>
[TestClass]
public sealed class AiBackgroundSessionAuthorizationTests
{
    [TestMethod]
    public async Task Authorize_returns_empty_when_binding_or_permission_is_invalid_async()
    {
        var binding = new SessionBindingSnapshot(Guid.CreateVersion7(), null, Guid.CreateVersion7(), "stamp", "host", "host");
        var bindingValidator = Substitute.For<IBackgroundSessionBindingValidator>();
        bindingValidator.IsValidAsync(binding, Arg.Any<CancellationToken>()).Returns(false);
        var permissions = Substitute.For<IPermissionSnapshotReader>();
        var authorization = new BackgroundSessionAuthorization(bindingValidator, permissions);
        Assert.IsNull(await authorization.AuthorizeAsync(binding, "ai.chat.read"));
    }

    [TestMethod]
    public async Task Authorize_returns_actor_when_binding_and_permission_match_async()
    {
        var binding = new SessionBindingSnapshot(Guid.CreateVersion7(), null, Guid.CreateVersion7(), "stamp", "host", "host");
        var bindingValidator = Substitute.For<IBackgroundSessionBindingValidator>();
        bindingValidator.IsValidAsync(binding, Arg.Any<CancellationToken>()).Returns(true);
        var permissions = Substitute.For<IPermissionSnapshotReader>();
        permissions.ReadAsync(binding.UserId, binding.ActorScope, binding.TenantId, Arg.Any<CancellationToken>())
            .Returns(new PermissionSnapshot(["ai.chat.read"], false));
        var authorization = new BackgroundSessionAuthorization(bindingValidator, permissions);
        var actor = await authorization.AuthorizeAsync(binding, "ai.chat.read");
        Assert.AreEqual(binding.UserId, actor!.UserId);
        Assert.AreEqual(binding.SessionId, actor.SessionId);
    }
}
