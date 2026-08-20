using Full.NET.Modules.Jobs.Contracts;

namespace Full.NET.Modules.Jobs.Execution.Handlers;

/// <summary>内置探针执行器：无 Args，用于集成测试与 Worker 烟囱验证。</summary>
internal sealed class PingJobExecutor : IJobHandlerExecutor
{
    public string HandlerKind => JobHandlerKinds.Ping;

    public Task ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(context.ArgsJson))
        {
            throw new InvalidOperationException("Ping jobs must not define args.");
        }

        return Task.CompletedTask;
    }
}
