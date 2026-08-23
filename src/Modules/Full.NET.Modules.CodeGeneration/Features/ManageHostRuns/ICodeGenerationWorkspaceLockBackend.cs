namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>
/// 跨实例工作区互斥后端；未启用分布式 Gate 时不调用。
/// </summary>
internal interface ICodeGenerationWorkspaceLockBackend
{
    /// <summary>
    /// 尝试获取工作区互斥锁；成功返回释放句柄，被占用或失败返回 null。
    /// 仅在分布式 Gate 启用时由 ApplyGate 调用，调用方必须在 finally 中释放返回的句柄。
    /// </summary>
    /// <param name="lockResource">由 <see cref="CodeGenerationWorkspaceLockResource.Create"/> 生成的稳定资源名。</param>
    /// <param name="cancellationToken">用于取消锁获取的令牌；已获取后释放不再受其约束。</param>
    /// <returns>成功时返回释放即解锁的 <see cref="IAsyncDisposable"/>；竞争失败返回 null。</returns>
    Task<IAsyncDisposable?> TryAcquireAsync(
        string lockResource,
        CancellationToken cancellationToken);
}