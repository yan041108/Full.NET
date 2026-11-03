using Microsoft.Extensions.Options;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Data.CodeGeneration.Generation;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>
/// 以持久化文件租约覆盖 Apply、Rollback 与清理；数据库会话锁作为附加协调，不能单独解除旧写入者的占用。
/// </summary>
internal sealed class CodeGenerationApplyGate(
    IOptions<CodeGenerationApplyOptions> options,
    ICodeGenerationWorkspaceLockBackend distributedLock)
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private IAsyncDisposable? _distributedLease;
    private GenerationWorkspaceLease? _workspaceLease;

    /// <summary>取得租约后可用的持久化身份，禁止以实例挂载路径代替。</summary>
    public Guid WorkspaceId => _workspaceLease?.WorkspaceId ?? throw new InvalidOperationException("尚未持有工作区租约。");

    internal GenerationWorkspaceLease Lease => _workspaceLease ?? throw new InvalidOperationException("尚未持有工作区租约。");

    /// <summary>
    /// 先进入本进程信号量与共享卷文件租约，再按持久化身份取得可选数据库会话锁。
    /// 分布式锁获取失败或取消时必须释放信号量，避免本进程被永久占位。成功后必须由调用方在 finally 等待 <see cref="ReleaseAsync"/>。
    /// </summary>
    /// <returns>成功进入临界区返回 true；本进程或跨实例已被占用返回 false。</returns>
    public async Task<bool> TryEnterAsync(CancellationToken cancellationToken)
    {
        if (!await _semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            _workspaceLease = await GenerationWorkspaceLease.TryAcquireAsync(options.Value.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
            if (_workspaceLease is null)
            {
                _semaphore.Release();
                return false;
            }
            if (options.Value.DistributedGateEnabled)
            {
                var resource = CodeGenerationWorkspaceLockResource.Create(_workspaceLease.WorkspaceId);
                _distributedLease = await distributedLock.TryAcquireAsync(resource, cancellationToken).ConfigureAwait(false);
                if (_distributedLease is null)
                {
                    await _workspaceLease.DisposeAsync().ConfigureAwait(false);
                    _workspaceLease = null;
                    _semaphore.Release();
                    return false;
                }
            }

            return true;
        }
        catch
        {
            try
            {
                if (_workspaceLease is not null) await _workspaceLease.DisposeAsync().ConfigureAwait(false);
                _workspaceLease = null;
            }
            finally { _semaphore.Release(); }
            throw;
        }
    }

    /// <summary>
    /// 释放临界区：先释放分布式租约（若持有）再释放信号量；必须与 <see cref="TryEnterAsync"/> 成对调用，否则信号量永久占用导致后续请求全部失败。
    /// </summary>
    public async ValueTask ReleaseAsync()
    {
        var lease = _distributedLease;
        var workspaceLease = _workspaceLease;
        _distributedLease = null;
        _workspaceLease = null;
        try
        {
            if (lease is not null)
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                if (workspaceLease is not null) await workspaceLease.DisposeAsync().ConfigureAwait(false);
            }
            finally { _semaphore.Release(); }
        }
    }
}
