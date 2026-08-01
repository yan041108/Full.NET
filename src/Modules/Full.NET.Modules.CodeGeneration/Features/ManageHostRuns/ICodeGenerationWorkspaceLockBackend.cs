namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>
/// 跨实例工作区互斥后端；未启用分布式 Gate 时不调用。
/// </summary>
internal interface ICodeGenerationWorkspaceLockBackend
{
    Task<IAsyncDisposable?> TryAcquireAsync(
        string lockResource,
        CancellationToken cancellationToken);
}