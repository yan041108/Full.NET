using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Streaming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>忽略取消的清理不能阻塞后续作用域，也不能提前释放仍在使用的作用域。</summary>
[TestClass]
public sealed class AiChatCleanupScopeTests
{
    [TestMethod]
    public async Task Disabled_tenant_can_finish_previously_authorized_generation_async()
    {
        var trusted = new TenantContext(Guid.NewGuid(), "tenant", "租户");
        var resolver = Substitute.For<IActiveTenantContextResolver>();
        await using var services = new ServiceCollection().AddSingleton(resolver)
            .AddScoped<ICurrentTenantContextWriter, CurrentTenantAccessor>().BuildServiceProvider();
        var cleanup = new AiChatCleanupScope(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiChatCleanupScope>.Instance);
        var called = false;
        Assert.IsTrue(await cleanup.RunAsync(trusted, (provider, _) =>
        {
            Assert.AreEqual(trusted.Id, provider.GetRequiredService<ICurrentTenantContextWriter>().Id);
            called = true;
            return Task.CompletedTask;
        }));
        Assert.IsTrue(called);
    }

    [TestMethod]
    public async Task Timed_out_cleanup_keeps_its_scope_until_driver_finishes_async()
    {
        await using var services = new ServiceCollection()
            .AddScoped<ICurrentTenantContextWriter, CurrentTenantAccessor>()
            .AddScoped<ScopeMarker>().BuildServiceProvider();
        var cleanup = new AiChatCleanupScope(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiChatCleanupScope>.Instance);
        var driver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScopeMarker? first = null;
        try
        {
            var result = await cleanup.RunAsync(null, async (provider, token) =>
            {
                first = provider.GetRequiredService<ScopeMarker>();
                Assert.IsTrue(token.CanBeCanceled);
                await driver.Task;
            }).WaitAsync(TimeSpan.FromSeconds(8));
            Assert.IsFalse(result);
            Assert.IsNotNull(first);
            Assert.IsFalse(first.Disposed.Task.IsCompleted);
            Assert.IsTrue(await cleanup.RunAsync(null, (provider, _) =>
            {
                Assert.AreNotSame(first, provider.GetRequiredService<ScopeMarker>());
                return Task.CompletedTask;
            }));
        }
        finally { driver.TrySetResult(); }
        await first!.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class ScopeMarker : IDisposable
    {
        internal TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Dispose() => Disposed.TrySetResult();
    }
}
