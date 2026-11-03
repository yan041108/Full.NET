using System.Text.Json.Serialization;
namespace Full.NET.Modules.Ai.Contracts;

/// <summary>创建持久 Agent 运行；ClientRequestId 用于幂等与预算关联。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。相同 ClientRequestId 重复提交返回同一运行标识，保证幂等。</remarks>
/// <param name="ClientRequestId">客户端幂等键；相同值的重复请求返回同一运行标识。</param>
/// <param name="DefinitionKey">Agent 定义的稳定键；发布后不可改名。</param>
/// <param name="ModelConfigId">使用的模型配置标识。</param>
/// <param name="Prompt">本次运行的输入提示文本。</param>
/// <param name="InputTokenLimit">输入 Token 预算预留上限；当前提供程序链路不实现实际输入分词硬限。</param>
/// <param name="OutputTokenLimit">输出 Token 预算预留上限；单次文本运行请求上限取该值与 4096 的较小值，实际用量仍由模型回执结算。</param>
/// <param name="PromptVersionId">可选当前发布的不可变纯文本版本；运行固定版本及停用代次，旧客户端省略时保留原协议。</param>
/// <param name="TargetChatSessionId">静态聊天工作流的明确目标；与登录会话身份分离，服务端复核所有权及当前读写权限。</param>
/// <param name="PersonalScheduleTarget">唯一完成图的准确日程目标；与聊天目标严格二选一。</param>
public sealed record CreateAiAgentRunRequest(
    Guid ClientRequestId,
    string DefinitionKey,
    Guid ModelConfigId,
    string Prompt,
    long InputTokenLimit,
    long OutputTokenLimit,
    Guid? PromptVersionId = null,
    Guid? TargetChatSessionId = null,
    AiPersonalScheduleCompletionTarget? PersonalScheduleTarget = null);

/// <summary>客户端只能指定准确目标与版本，不能覆盖执行主体、租户或审批事实。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiPersonalScheduleCompletionTarget(Guid Id, int Version);

/// <summary>准确原操作状态；accepted/unknown均不代表日程已经完成，正文不进入响应。</summary>
public sealed record AiPersonalScheduleCompletionStatus(Guid RunId, Guid OperationId, Guid ScheduleId, int ExpectedVersion,
    string State, string? Reason, int? ActualVersion, DateTimeOffset? CompletedAtUtc);

/// <summary>真实静态Run等待审批的完成建议，只有服务端重建的原参数可提交批准。</summary>
public sealed record AiPersonalScheduleCompletionProposal(Guid RunId, Guid OperationId, Guid ScheduleId, int ExpectedVersion,
    string ToolName, int ToolVersion, string ArgumentsJson);

/// <summary>202 接受响应，仅返回运行标识。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="RunId">新建运行的稳定标识。</param>
public sealed record CreateAiAgentRunResponse(Guid RunId);

/// <summary>本人等待审批的静态工作流提案；展示不代表已经批准或执行。</summary>
/// <param name="RunId">原运行身份。</param>
/// <param name="OperationId">原写节点的稳定操作身份。</param>
/// <param name="TargetChatSessionId">明确的目标聊天资源。</param>
/// <param name="CurrentTitle">当前权威标题。</param>
/// <param name="TargetVersion">当前权威资源版本，最终审批仍会重新读取。</param>
/// <param name="ProposedTitle">已完成模型节点提出的标题。</param>
/// <param name="ToolName">固定写工具名称。</param>
/// <param name="ToolVersion">固定写工具版本。</param>
/// <param name="ArgumentsJson">服务端从原检查点重建的固定参数，不含主体或租户。</param>
public sealed record AiAgentWorkflowProposalResponse(Guid RunId, Guid OperationId, Guid TargetChatSessionId,
    string CurrentTitle, int TargetVersion, string ProposedTitle, string ToolName, int ToolVersion, string ArgumentsJson);

/// <summary>本人可读的运行摘要。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">运行稳定标识。</param>
/// <param name="StatusKey">运行状态键（如 queued、running、completed、failed）；发布后不可改名。</param>
/// <param name="DefinitionKey">Agent 定义的稳定键。</param>
/// <param name="DefinitionVersion">Agent 定义版本号。</param>
/// <param name="DeadlineAtUtc">运行最晚完成时间（UTC）；超时后自动失败。</param>
/// <param name="CreatedAtUtc">运行创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">运行最近更新时间（UTC）。</param>
public sealed record AiAgentRunResponse(
    Guid Id,
    string StatusKey,
    string DefinitionKey,
    int DefinitionVersion,
    DateTimeOffset DeadlineAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
