using Full.NET.Modules.Jobs.Contracts;

namespace Full.NET.Modules.Jobs.Execution.Handlers;

/// <summary>内置探针任务：用于集成测试与 Worker 烟囱验证。</summary>
internal sealed class PingJobHandler : IJobHandler
{
    public string JobKey => JobsWellKnownKeys.Ping;

    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
