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
