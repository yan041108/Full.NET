namespace Full.NET.Modules.Jobs.Execution;

/// <summary>按 HandlerKind 注册的内置任务执行器。</summary>
public interface IJobHandlerExecutor
{
    /// <summary>执行器对应的稳定 HandlerKind，用于任务路由匹配。</summary>
    string HandlerKind { get; }

    Task ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken);
}
