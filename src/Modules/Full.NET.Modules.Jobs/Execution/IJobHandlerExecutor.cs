namespace Full.NET.Modules.Jobs.Execution;

/// <summary>按 HandlerKind 注册的内置任务执行器。</summary>
public interface IJobHandlerExecutor
{
    string HandlerKind { get; }

    Task ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken);
}
