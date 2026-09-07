using Full.NET.Modules.Ai.Streaming;

namespace Full.NET.UnitTests.Ai;

/// <summary>本地取消注册按生成标识持有，旧请求清理不影响后继请求。</summary>
[TestClass]
public sealed class AiChatGenerationRegistryTests
{
    /// <summary>替换后旧请求的 finally 不得删除新请求的取消入口。</summary>
    [TestMethod]
    public void Old_generation_cleanup_preserves_successor()
    {
        var registry = new AiChatGenerationRegistry();
        var session = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var oldToken = registry.Register(session, first, default);
        var newToken = registry.Register(session, second, default);
        Assert.IsTrue(oldToken.IsCancellationRequested);
        registry.Unregister(session, first);
        Assert.IsFalse(registry.TryCancel(session, first));
        Assert.IsFalse(newToken.IsCancellationRequested);
        Assert.IsTrue(registry.TryCancel(session, second));
        Assert.IsTrue(newToken.IsCancellationRequested);
        registry.Unregister(session, second);
        Assert.IsFalse(registry.TryCancel(session, second));
    }

    /// <summary>触发取消不会过早释放仍由执行任务使用的令牌源。</summary>
    [TestMethod]
    public void Cancellation_keeps_registration_until_owner_finishes()
    {
        var registry = new AiChatGenerationRegistry();
        var session = Guid.NewGuid();
        var generation = Guid.NewGuid();
        var token = registry.Register(session, generation, default);
        Assert.IsTrue(registry.TryCancel(session, generation));
        using var callback = token.Register(() => { });
        Assert.IsTrue(registry.TryCancel(session, generation));
        registry.Unregister(session, generation);
        Assert.IsFalse(registry.TryCancel(session, generation));
    }
}
