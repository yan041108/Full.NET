using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>知识读取使用集合授权端口，不能退回逐权限串行重查。</summary>
[TestClass]
public sealed class AiKnowledgePermissionSetTests
{
    [TestMethod]
    public async Task Guard_checks_the_full_set_once_and_rechecks_next_boundary()
    {
        var actor = Guid.CreateVersion7();
        var auth = Substitute.For<ICurrentSessionAuthorization>();
        auth.AuthorizeAllAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AuthorizedSessionActor?>(new AuthorizedSessionActor(actor, null, Guid.CreateVersion7())),
                Task.FromResult<AuthorizedSessionActor?>(null));
        var guard = new AiKnowledgePermissionGuard(auth, Substitute.For<IHttpContextAccessor>(), Substitute.For<ICurrentApiKeyAuthorization>());
        string[] required = ["a", "b", "c", "d"];
        Assert.IsTrue(await guard.IsAuthorizedAsync(required, actor, default));
        Assert.IsFalse(await guard.IsAuthorizedAsync(required, actor, default));
        await auth.Received(2).AuthorizeAllAsync(Arg.Is<IReadOnlyCollection<string>>(p => p != null && p.SequenceEqual(required)), Arg.Any<CancellationToken>());
        await auth.DidNotReceive().AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
