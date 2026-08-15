using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modularity.Messaging;

/// <summary>
/// CQRS 查询分发器默认实现；与 <see cref="CommandDispatcher"/> 共享相同的 Behavior 管道机制，
/// 但不参与数据库事务（查询天然只读，无需事务包裹）。
/// </summary>
/// <remarks>
/// 设计边界：查询端只负责读取与投影，不触发领域事件写入；
/// 若需要在查询后产生副作用，应通过应用层显式调用 Command 通道，而非扩展 Dispatcher。
/// </remarks>
public sealed class QueryDispatcher(IServiceProvider services) : IQueryDispatcher
{
    /// <summary>
    /// 分发查询并构建 Behavior 管道；从 DI 容器解析匹配的 <c>IQueryHandler</c> 执行。
    /// </summary>
    /// <typeparam name="TQuery">查询类型，需匹配已注册的 <c>IQueryHandler</c>。</typeparam>
    /// <typeparam name="TResult">查询结果类型。</typeparam>
    /// <exception cref="InvalidOperationException">未注册对应 Handler。</exception>
    public Task<Result<TResult>> SendAsync<TQuery, TResult>(
        TQuery query,
        CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResult>
    {
        DispatchHandlerDelegate<TResult> pipeline =
            ct => HandleCoreAsync<TQuery, TResult>(query, ct);

        foreach (var behavior in services
                     .GetServices<IDispatchBehavior<TQuery, TResult>>()
                     .Reverse())
        {
            var next = pipeline;
            pipeline = ct => behavior.HandleAsync(query, next, ct);
        }

        return pipeline(cancellationToken);
    }

    private Task<Result<TResult>> HandleCoreAsync<TQuery, TResult>(
        TQuery query,
        CancellationToken cancellationToken)
        where TQuery : IQuery<TResult> =>
        services.GetRequiredService<IQueryHandler<TQuery, TResult>>()
            .HandleAsync(query, cancellationToken);
}
