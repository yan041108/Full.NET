using Full.NET.Abstractions.Results;

namespace Full.NET.Abstractions.Messaging;

/// <summary>
/// 表示会改变系统状态的命令消息契约，返回指定类型的处理结果。
/// </summary>
/// <remarks>
/// 实现该接口的类型作为 Command Handler 的输入，承载业务意图所需的全部参数；
/// 命令通常与事务、授权和验证行为绑定。
/// </remarks>
/// <typeparam name="TResult">命令执行完成后的返回值类型。</typeparam>
public interface ICommand<TResult>;

/// <summary>
/// 标记需要在显式事务边界内执行的命令。
/// </summary>
/// <remarks>
/// 实现该接口的命令将被调度器或 Behavior 包裹在数据库事务中，确保命令处理与
/// 集成事件等副作用的原子提交；具体事务隔离级别由配置决定。
/// </remarks>
public interface ITransactionalCommand;

/// <summary>
/// 表示需要事务且返回结果的命令，同时满足 <see cref="ICommand{TResult}"/> 与事务契约。
/// </summary>
/// <typeparam name="TResult">命令执行完成后的返回值类型。</typeparam>
public interface ITransactionalCommand<TResult> : ICommand<TResult>, ITransactionalCommand;

/// <summary>
/// 定义命令处理器的异步契约，接收命令并返回操作结果。
/// </summary>
/// <remarks>
/// 每个具体命令应对应唯一的 Handler 实现；若需注入跨切面关注点（日志、验证、事务），
/// 应通过 <see cref="IDispatchBehavior{TMessage, TResult}"/> 扩展而非在 Handler 内部硬编码。
/// </remarks>
/// <typeparam name="TCommand">待处理的命令类型。</typeparam>
/// <typeparam name="TResult">命令处理结果的值类型。</typeparam>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    /// <summary>
    /// 异步处理指定命令并返回结果。
    /// </summary>
    /// <param name="command">包含业务参数的命令实例。</param>
    /// <param name="cancellationToken">用于取消命令处理的令牌。</param>
    /// <returns>封装成功值或错误的结果实例。</returns>
    Task<Result<TResult>> HandleAsync(
        TCommand command,
        CancellationToken cancellationToken);
}

/// <summary>
/// 定义命令调度器契约，负责将命令路由到匹配的 Handler 并执行 Behavior 管道。
/// </summary>
public interface ICommandDispatcher
{
    /// <summary>
    /// 异步发送命令到对应的 Handler，按顺序执行已注册的 Behavior 管道。
    /// </summary>
    /// <typeparam name="TCommand">命令类型。</typeparam>
    /// <typeparam name="TResult">命令返回值类型。</typeparam>
    /// <param name="command">待调度的命令实例。</param>
    /// <param name="cancellationToken">用于取消调度流程的令牌。</param>
    /// <returns>封装成功值或错误的结果实例。</returns>
    Task<Result<TResult>> SendAsync<TCommand, TResult>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>;
}
