namespace Full.NET.Data.Abstractions;

/// <summary>
/// 提供由当前数据库会话持有的跨实例互斥锁；释放句柄即释放数据库锁和专属连接。
/// </summary>
public interface IDatabaseSessionLock
{
    /// <summary>
    /// 尝试立即获取指定资源的会话锁；资源已被占用时返回 <see langword="null"/>。
    /// </summary>
    /// <param name="resource">待锁定的资源稳定键。</param>
    /// <param name="cancellationToken">用于取消获取操作的令牌。</param>
    /// <returns>异步结果；获取成功返回锁的 <see cref="IAsyncDisposable"/> 句柄，释放即解锁；占用或取消时返回 <see langword="null"/>。</returns>
    /// <remarks>
    /// 该锁为非阻塞尝试，不会等待资源释放；调用方需自行处理 <see langword="null"/> 返回值的重试或降级逻辑。
    /// 锁与专属数据库会话绑定，进程崩溃或连接断开时由数据库自动释放。
    /// </remarks>
    Task<IAsyncDisposable?> TryAcquireAsync(
        string resource,
        CancellationToken cancellationToken = default);
}

/// <summary>需要在长耗时控制操作中检查原数据库会话的锁句柄；无法确认必须停止执行。</summary>
public interface IDatabaseSessionLockLease : IAsyncDisposable
{
    /// <summary>在持锁的同一专属连接校验；连接丢失后不得重连并重新获取锁。</summary>
    Task EnsureHeldAsync(CancellationToken cancellationToken = default);
}

/// <summary>将会话锁与当前作用域后续命令事务绑定到同一物理连接；连接丢失时事务不能提交。</summary>
public interface IDatabaseTransactionBoundSessionLock
{
    /// <summary>在无活动事务的作用域中获取专属锁和连接；句柄必须持续持有到事务结束。</summary>
    Task<IDatabaseSessionLockLease?> TryAcquireAsync(string resource, CancellationToken cancellationToken = default);
}
