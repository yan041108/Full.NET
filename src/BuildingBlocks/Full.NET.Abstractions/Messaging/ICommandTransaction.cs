using Full.NET.Abstractions.Results;

namespace Full.NET.Abstractions.Messaging;

/// <summary>
/// 命令级事务协调器：把多个命令写操作包裹在同一个本地数据库事务中，
/// 保证业务状态变更与 Outbox 写入的原子提交。
/// </summary>
/// <remarks>
/// 实现应在进入时打开连接并 BeginTransaction，在 <paramref name="action"/>
/// 成功返回时 Commit，抛出未处理异常时 Rollback。事务隔离级别由配置决定，
/// 通常使用 ReadCommitted；不得在 <paramref name="action"/> 内手动管理连接或事务。
/// </remarks>
public interface ICommandTransaction
{
    /// <summary>
    /// 在当前作用域事务内异步执行指定操作并返回结果。
    /// </summary>
    /// <typeparam name="T">操作返回值类型。</typeparam>
    /// <param name="action">需要在事务中执行的异步委托。</param>
    /// <param name="cancellationToken">用于取消事务与操作的令牌。</param>
    /// <returns><paramref name="action"/> 的返回结果。</returns>
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);

    /// <summary>
    /// 在当前作用域事务内异步执行返回 <see cref="Result{T}"/> 的操作，保持失败传播语义。
    /// </summary>
    /// <typeparam name="T">成功时的值类型。</typeparam>
    /// <param name="action">需要在事务中执行的异步 Result 委托。</param>
    /// <param name="cancellationToken">用于取消事务与操作的令牌。</param>
    /// <returns>封装成功值或错误的结果实例。</returns>
    Task<Result<T>> ExecuteResultAsync<T>(
        Func<CancellationToken, Task<Result<T>>> action,
        CancellationToken cancellationToken) =>
        ExecuteAsync(action, cancellationToken);
}