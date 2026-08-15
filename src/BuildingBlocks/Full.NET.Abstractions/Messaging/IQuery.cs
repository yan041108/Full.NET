using Full.NET.Abstractions.Results;

namespace Full.NET.Abstractions.Messaging;

/// <summary>
/// 表示只读查询消息契约，用于获取系统状态且不产生副作用。
/// </summary>
/// <remarks>
/// 实现该接口的类型作为 Query Handler 的输入，承载查询条件、分页参数和投影选项；
/// 查询不应修改业务数据，允许被缓存或采用只读副本执行。
/// </remarks>
/// <typeparam name="TResult">查询返回的结果类型。</typeparam>
public interface IQuery<TResult>;

/// <summary>
/// 定义查询处理器的异步契约，接收查询并返回封装结果。
/// </summary>
/// <remarks>
/// 每个具体查询应对应唯一的 Handler 实现；若需注入日志、缓存或权限检查等跨切面关注点，
/// 应通过 <see cref="IDispatchBehavior{TMessage, TResult}"/> 扩展。
/// </remarks>
/// <typeparam name="TQuery">待处理的查询类型。</typeparam>
/// <typeparam name="TResult">查询结果的值类型。</typeparam>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    /// <summary>
    /// 异步处理指定查询并返回结果。
    /// </summary>
    /// <param name="query">包含查询参数的查询实例。</param>
    /// <param name="cancellationToken">用于取消查询处理的令牌。</param>
    /// <returns>封装成功值或错误的结果实例。</returns>
    Task<Result<TResult>> HandleAsync(
        TQuery query,
        CancellationToken cancellationToken);
}

/// <summary>
/// 定义查询调度器契约，负责将查询路由到匹配的 Handler 并执行 Behavior 管道。
/// </summary>
public interface IQueryDispatcher
{
    /// <summary>
    /// 异步发送查询到对应的 Handler，按顺序执行已注册的 Behavior 管道。
    /// </summary>
    /// <typeparam name="TQuery">查询类型。</typeparam>
    /// <typeparam name="TResult">查询返回值类型。</typeparam>
    /// <param name="query">待调度的查询实例。</param>
    /// <param name="cancellationToken">用于取消调度流程的令牌。</param>
    /// <returns>封装成功值或错误的结果实例。</returns>
    Task<Result<TResult>> SendAsync<TQuery, TResult>(
        TQuery query,
        CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResult>;
}
