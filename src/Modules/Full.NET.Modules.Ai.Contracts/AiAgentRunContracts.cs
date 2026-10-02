namespace Full.NET.Modules.Ai.Contracts;

/// <summary>创建持久 Agent 运行；ClientRequestId 用于幂等与预算关联。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。相同 ClientRequestId 重复提交返回同一运行标识，保证幂等。</remarks>
/// <param name="ClientRequestId">客户端幂等键；相同值的重复请求返回同一运行标识。</param>
/// <param name="DefinitionKey">Agent 定义的稳定键；发布后不可改名。</param>
/// <param name="ModelConfigId">使用的模型配置标识。</param>
/// <param name="Prompt">本次运行的输入提示文本。</param>
/// <param name="InputTokenLimit">输入 Token 上限；超出时请求被拒绝。</param>
/// <param name="OutputTokenLimit">输出 Token 上限；超出时生成被截断。</param>
public sealed record CreateAiAgentRunRequest(
    Guid ClientRequestId,
    string DefinitionKey,
    Guid ModelConfigId,
    string Prompt,
    long InputTokenLimit,
    long OutputTokenLimit);

/// <summary>202 接受响应，仅返回运行标识。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="RunId">新建运行的稳定标识。</param>
public sealed record CreateAiAgentRunResponse(Guid RunId);

/// <summary>本人可读的运行摘要。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">运行稳定标识。</param>
/// <param name="StatusKey">运行状态键（如 Pending、Running、Succeeded、Failed）；发布后不可改名。</param>
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
