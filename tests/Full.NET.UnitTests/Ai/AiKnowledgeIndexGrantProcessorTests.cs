using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>收费授权扫描的可信范围在成功、失败和取消后都清除，单项失败不遗留租户。</summary>
[TestClass]
public sealed class AiKnowledgeIndexGrantProcessorTests
{
    [TestMethod]
    [DataRow("empty")][DataRow("failure")][DataRow("cancelled")]
    public async Task Root_scope_is_cleared_on_every_exit(string outcome)
    {
        var contexts = new List<ICurrentTenantContextWriter>();
        var query = new ScanQuery(_ => outcome switch
            {
                "failure" => Task.FromException<IReadOnlyList<AiKnowledgeIndexGrantAnchor>>(new InvalidDataException("controlled scan failure")),
                "cancelled" => Task.FromCanceled<IReadOnlyList<AiKnowledgeIndexGrantAnchor>>(new CancellationToken(true)),
                _ => Task.FromResult<IReadOnlyList<AiKnowledgeIndexGrantAnchor>>([])
            });
        using var provider = Services(query, contexts).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var processor = ActivatorUtilities.CreateInstance<AiKnowledgeIndexGrantProcessor>(provider);
        if (outcome == "failure") await Assert.ThrowsExactlyAsync<InvalidDataException>(() => processor.ProcessAsync(4, default));
        else if (outcome == "cancelled") await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => processor.ProcessAsync(4, default));
        else Assert.AreEqual(0, await processor.ProcessAsync(4, default));
        Assert.HasCount(1, contexts);
        contexts[0].Received(1).SetHost();
        contexts[0].Received(1).Clear();
    }

    [TestMethod]
    [DataRow(false)][DataRow(true)]
    public async Task Failed_item_clears_independent_persisted_scope(bool tenantScoped)
    {
        var contexts = new List<ICurrentTenantContextWriter>();
        Guid? tenantId = tenantScoped ? Guid.CreateVersion7() : null;
        var query = new ScanQuery(_ => Task.FromResult<IReadOnlyList<AiKnowledgeIndexGrantAnchor>>([new(Guid.CreateVersion7(), tenantId)]));
        // 不注册授权存储，定向制造单项服务解析失败，验证失败路径的上下文清理。
        using var provider = Services(query, contexts).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var processor = ActivatorUtilities.CreateInstance<AiKnowledgeIndexGrantProcessor>(provider);
        Assert.AreEqual(1, await processor.ProcessAsync(4, default));
        Assert.HasCount(2, contexts);
        if (tenantScoped) contexts[1].Received(1).SetTenant(Arg.Is<TenantContext>(x => x != null && x.Id == tenantId));
        else contexts[1].Received(1).SetHost();
        foreach (var context in contexts) context.Received(1).Clear();
    }

    private static IServiceCollection Services(IQueryExecutor query, List<ICurrentTenantContextWriter> contexts) => new ServiceCollection()
        .AddSingleton(query)
        .AddSingleton(Substitute.For<IDataTransactionState>())
        .AddSingleton(Substitute.For<IClock>())
        .AddSingleton(Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }))
        .AddSingleton(Options.Create(new AiKnowledgeIndexGrantOptions { Enabled = true }))
        .AddSingleton<Microsoft.Extensions.Logging.ILogger<AiKnowledgeIndexGrantProcessor>>(NullLogger<AiKnowledgeIndexGrantProcessor>.Instance)
        .AddScoped(_ => { var context = Substitute.For<ICurrentTenantContextWriter>(); contexts.Add(context); return context; });

    // 内部投影使用显式查询桩，避免代理库为自动返回值动态生成不可见泛型元素的集合代理。
    private sealed class ScanQuery(Func<CancellationToken, Task<IReadOnlyList<AiKnowledgeIndexGrantAnchor>>> scan) : IQueryExecutor
    {
        public async Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (typeof(T) != typeof(AiKnowledgeIndexGrantAnchor)) throw new InvalidOperationException("Unexpected scan projection.");
            return (IReadOnlyList<T>)(object)await scan(cancellationToken);
        }

        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Unexpected single query.");
    }
}
