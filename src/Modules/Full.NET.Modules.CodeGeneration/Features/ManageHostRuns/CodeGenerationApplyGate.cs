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
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private IAsyncDisposable? _distributedLease;

    /// <summary>
    /// 先以非阻塞信号量串行化本进程 Apply/Rollback；若启用分布式 Gate 再叠加数据库会话锁跨实例互斥。
    /// 分布式锁获取失败或取消时必须释放信号量，避免本进程被永久占位。成功后必须由调用方在 finally 等待 <see cref="ReleaseAsync"/>。
    /// </summary>
    /// <returns>成功进入临界区返回 true；本进程或跨实例已被占用返回 false。</returns>
    public async Task<bool> TryEnterAsync(CancellationToken cancellationToken)
    {
        if (!await _semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (!options.Value.DistributedGateEnabled)
        {
            return true;
        }

        try
        {
            var resource = CodeGenerationWorkspaceLockResource.Create(options.Value.WorkspaceRoot);
            _distributedLease = await distributedLock.TryAcquireAsync(resource, cancellationToken).ConfigureAwait(false);
            if (_distributedLease is null)
            {
                _semaphore.Release();
                return false;
            }

            return true;
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }

    /// <summary>
    /// 释放临界区：先释放分布式租约（若持有）再释放信号量；必须与 <see cref="TryEnterAsync"/> 成对调用，否则信号量永久占用导致后续请求全部失败。
    /// </summary>
    public async ValueTask ReleaseAsync()
    {
        var lease = _distributedLease;
        _distributedLease = null;
        try
        {
            if (lease is not null)
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            // 释放异常也不能使本地 Gate 永久占位；异常仍向调用方传播。
            _semaphore.Release();
        }
    }
}
