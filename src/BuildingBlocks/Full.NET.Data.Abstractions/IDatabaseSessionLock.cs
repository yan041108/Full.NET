namespace Full.NET.Data.Abstractions;

/// <summary>
/// 提供由当前数据库会话持有的跨实例互斥锁；释放句柄即释放数据库锁和专属连接。
/// </summary>
public interface IDatabaseSessionLock
{
    /// <summary>
    /// 尝试立即获取指定资源的会话锁；资源已被占用时返回 <see langword="null"/>。
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(
        string resource,
        CancellationToken cancellationToken = default);
}
