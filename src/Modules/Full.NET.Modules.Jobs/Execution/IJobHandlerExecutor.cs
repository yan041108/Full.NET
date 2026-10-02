namespace Full.NET.Modules.Jobs.Execution;

/// <summary>按 HandlerKind 注册的内置任务执行器。</summary>
public interface IJobHandlerExecutor
{
    /// <summary>执行器对应的稳定 HandlerKind，用于任务路由匹配。</summary>
    string HandlerKind { get; }

    /// <summary>
    /// 执行单次任务；实现必须按 <see cref="HandlerKind"/> 解析参数并在异常时返回可重试的失败语义。
    /// </summary>
    /// <param name="context">单次任务执行上下文，包含执行标识、作业定义、HandlerKind、参数 JSON 与触发方式等。</param>
    /// <param name="cancellationToken">用于取消任务执行的令牌。</param>
    Task ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken);
}
