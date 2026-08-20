namespace Full.NET.Modules.Jobs.Execution;

/// <summary>单次任务执行上下文；由 Runner 在领取后构造并传给执行器。</summary>
public sealed record JobExecutionContext(
    Guid ExecutionId,
    Guid JobDefinitionId,
    string JobKey,
    string HandlerKind,
    string? ArgsJson,
    string TriggerKind);
