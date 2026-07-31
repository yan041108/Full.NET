namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>
/// 在单个 API 进程内串行化本地工作区 Apply；跨实例执行继续保守留给后续 Worker。
/// </summary>
internal sealed class CodeGenerationApplyGate
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    public Task<bool> TryEnterAsync(CancellationToken cancellationToken) =>
        semaphore.WaitAsync(0, cancellationToken);

    public void Release() => semaphore.Release();
}
