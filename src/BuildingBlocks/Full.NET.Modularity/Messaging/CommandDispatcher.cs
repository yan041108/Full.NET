using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modularity.Messaging;

/// <summary>
/// CQRS 命令分发器默认实现；负责从 DI 容器解析匹配的 <c>ICommandHandler</c>，
/// 并按注册顺序逆序串联所有 <c>IDispatchBehavior</c> 形成俄罗斯套娃式管道。
/// </summary>
/// <remarks>
/// 管道构造机制：以 Handler 执行为最内层，逐个将 Behavior 包裹为外层委托，
/// 因此注册顺序靠前的 Behavior 先执行（类似 ASP.NET Core 中间件顺序）。
/// 对标记 <c>ITransactionalCommand</c> 的命令，自动包裹在 <c>ICommandTransaction</c> 事务中执行。
/// </remarks>
public sealed class CommandDispatcher(
    IServiceProvider services,
    ICommandTransaction? transaction = null) : ICommandDispatcher
{
    /// <summary>
    /// 分发命令并构建 Behavior 管道；对事务型命令在同一数据库事务内调用 Handler。
    /// </summary>
    /// <typeparam name="TCommand">命令类型，需匹配已注册的 <c>ICommandHandler</c>。</typeparam>
    /// <typeparam name="TResult">命令返回结果类型。</typeparam>
    /// <param name="command">待分发的命令实例。</param>
    /// <param name="cancellationToken">用于取消 Handler 与 Behavior 管道的令牌。</param>
    /// <returns>包含成功值或结构化错误的应用层结果。</returns>
    /// <exception cref="InvalidOperationException">缺少 Handler 或事务命令未注册事务组件。</exception>
    public Task<Result<TResult>> SendAsync<TCommand, TResult>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>
    {
        DispatchHandlerDelegate<TResult> pipeline =
            ct => HandleCoreAsync<TCommand, TResult>(command, ct);

        foreach (var behavior in services
                     .GetServices<IDispatchBehavior<TCommand, TResult>>()
                     .Reverse())
        {
            var next = pipeline;
            pipeline = ct => behavior.HandleAsync(command, next, ct);
        }

        return pipeline(cancellationToken);
    }

    private Task<Result<TResult>> HandleCoreAsync<TCommand, TResult>(
        TCommand command,
        CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>
    {
        var handler = services.GetRequiredService<ICommandHandler<TCommand, TResult>>();

        if (command is ITransactionalCommand)
        {
            return (transaction ?? throw new InvalidOperationException(
                    "No command transaction is registered."))
                .ExecuteAsync(
                    ct => handler.HandleAsync(command, ct),
                    cancellationToken);
        }

        return handler.HandleAsync(command, cancellationToken);
    }
}
