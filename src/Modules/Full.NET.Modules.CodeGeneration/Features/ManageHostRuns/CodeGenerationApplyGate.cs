using Microsoft.Extensions.Options;
using Full.NET.Modules.CodeGeneration.Configuration;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>
/// 在单个进程内串行化本地工作区 Apply/Rollback；可选叠加数据库会话锁跨实例互斥。
/// </summary>
internal sealed class CodeGenerationApplyGate(
    IOptions<CodeGenerationApplyOptions> options,
    ICodeGenerationWorkspaceLockBackend distributedLock)
{
    private readonly SemaphoreSlim semaphore = new(1, 1);
    private IAsyncDisposable? distributedLease;

    /// <summary>
    /// 先以非阻塞信号量串行化本进程 Apply/Rollback；若启用分布式 Gate 再叠加数据库会话锁跨实例互斥。
    /// 分布式锁获取失败时必须先释放信号量再返回 false，避免本进程被永久占位。成功后必须由调用方在 finally 调用 <see cref="Release"/>。
    /// </summary>
    /// <returns>成功进入临界区返回 true；本进程或跨实例已被占用返回 false。</returns>
    public async Task<bool> TryEnterAsync(CancellationToken cancellationToken)
    {
        if (!await semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (!options.Value.DistributedGateEnabled)
        {
            return true;
        }

        var resource = CodeGenerationWorkspaceLockResource.Create(
            options.Value.WorkspaceRoot);
        distributedLease = await distributedLock.TryAcquireAsync(
            resource,
            cancellationToken).ConfigureAwait(false);
        if (distributedLease is null)
        {
            semaphore.Release();
            return false;
        }

        return true;
    }

    /// <summary>
    /// 释放临界区：先释放分布式租约（若持有）再释放信号量；必须与 <see cref="TryEnterAsync"/> 成对调用，否则信号量永久占用导致后续请求全部失败。
    /// </summary>
    public void Release()
    {
        var lease = distributedLease;
        distributedLease = null;
        if (lease is not null)
        {
            lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        semaphore.Release();
    }
}
