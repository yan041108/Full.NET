namespace Full.NET.Modules.Workflow.Features.ManageMyCc;

/// <summary>返回给当前用户的工作流抄送知识记录。</summary>
internal sealed record WorkflowCcResponse(
    Guid Id,
    Guid InstanceId,
    Guid? StepId,
    string NodeKey,
    string BusinessType,
    string BusinessId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadAtUtc);

/// <summary>抄送首次已读动作的幂等结果。</summary>
internal sealed record WorkflowCcReadResponse(
    Guid Id,
    DateTimeOffset ReadAtUtc);
