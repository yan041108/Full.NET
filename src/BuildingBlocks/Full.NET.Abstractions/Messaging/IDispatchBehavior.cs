using Full.NET.Abstractions.Results;

namespace Full.NET.Abstractions.Messaging;

/// <summary>
/// 表示调度管道中传递到下一环的处理委托，最终会调用实际的 Handler。
/// </summary>
/// <remarks>
/// Behavior 实现方应在调用 <c>next</c> 前后注入横切逻辑，并负责传播
/// <see cref="CancellationToken"/>；未调用 <c>next</c> 表示短路管道。
/// </remarks>
/// <typeparam name="TResult">最终返回结果的值类型。</typeparam>
public delegate Task<Result<TResult>> DispatchHandlerDelegate<TResult>(
    CancellationToken cancellationToken);

/// <summary>
/// 定义消息调度管道中 Behavior 的契约，用于在 Handler 执行前后插入横切关注点。
/// </summary>
/// <remarks>
/// 典型实现包括：日志记录、参数校验、事务包装、权限检查、缓存、性能度量与重试策略。
/// Behavior 按注册顺序嵌套调用，最内层为实际 Handler。
/// </remarks>
/// <typeparam name="TMessage">被拦截的消息类型（Command 或 Query）。</typeparam>
/// <typeparam name="TResult">消息处理返回的结果值类型。</typeparam>
public interface IDispatchBehavior<in TMessage, TResult>
{
    /// <summary>
    /// 异步执行 Behavior 逻辑，并通过 <paramref name="next"/> 调用管道的下一环。
    /// </summary>
    /// <param name="message">正在处理的消息实例。</param>
    /// <param name="next">管道下一环的委托。</param>
    /// <param name="cancellationToken">用于取消处理流程的令牌。</param>
    /// <returns>封装成功值或错误的结果实例。</returns>
    Task<Result<TResult>> HandleAsync(
        TMessage message,
        DispatchHandlerDelegate<TResult> next,
        CancellationToken cancellationToken);
}
