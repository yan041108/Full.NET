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
