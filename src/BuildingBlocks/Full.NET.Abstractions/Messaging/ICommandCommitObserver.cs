namespace Full.NET.Abstractions.Messaging;

/// <summary>为命令事务注册提交后的临时观测，不承担可靠业务通知或持久化投递。</summary>
/// <remarks>
/// 与 ICommandTransaction 共享同一作用域；只能在该协调器拥有的活动事务中注册。
/// 最外层事务成功提交后按注册顺序调用，回滚或提交失败时丢弃；观测异常不改变业务结果。
/// 不得用于外部业务调用、缓存失效或 Outbox 替代品，不保证进程崩溃后的投递。
/// </remarks>
public interface ICommandCommitObserver
{
    /// <summary>注册仅在最外层提交成功后执行的同步观测。</summary>
    /// <param name="observation">不依赖事务资源的指标观测；不得捕获需可靠执行的业务操作。</param>
    /// <exception cref="ArgumentNullException">观测为空。</exception>
    /// <exception cref="InvalidOperationException">不处于同一命令协调器拥有的活动事务。</exception>
    void Observe(Action observation);
}
