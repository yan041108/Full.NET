namespace Full.NET.Modules.Jobs.Execution;

/// <summary>单次任务执行上下文；由 Runner 在领取后构造并传给执行器。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="ExecutionId">本次执行记录的稳定标识，用于日志与取消请求定位。</param>
/// <param name="JobDefinitionId">关联的作业定义标识。</param>
/// <param name="JobKey">作业稳定业务键；用于跨模块事件与审计关联。</param>
/// <param name="HandlerKind">执行器稳定机器码，取值自 JobHandlerKinds。</param>
/// <param name="ArgsJson">执行参数 JSON 文本；执行器按 HandlerKind 反序列化，可为 <see langword="null"/>。</param>
/// <param name="TriggerKind">触发方式稳定机器码，取值自 JobTriggerKinds。</param>
public sealed record JobExecutionContext(
    Guid ExecutionId,
    Guid JobDefinitionId,
    string JobKey,
    string HandlerKind,
    string? ArgsJson,
    string TriggerKind);
