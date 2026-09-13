namespace Full.NET.Modules.Ai.Contracts;

/// <summary>创建持久 Agent 运行；ClientRequestId 用于幂等与预算关联。</summary>
public sealed record CreateAiAgentRunRequest(
    Guid ClientRequestId,
    string DefinitionKey,
    Guid ModelConfigId,
    string Prompt,
    long InputTokenLimit,
    long OutputTokenLimit);

/// <summary>202 接受响应，仅返回运行标识。</summary>
public sealed record CreateAiAgentRunResponse(Guid RunId);

/// <summary>本人可读的运行摘要。</summary>
public sealed record AiAgentRunResponse(
    Guid Id,
    string StatusKey,
    string DefinitionKey,
    int DefinitionVersion,
    DateTimeOffset DeadlineAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
