namespace Full.NET.Modules.Ai.Contracts;

/// <summary>创建持久委托；至少限定工具名或权限码之一。</summary>
public sealed record CreateAiAgentDelegationRequest(
    Guid GranteeUserId,
    string? ToolName,
    string? PermissionCode,
    DateTimeOffset ExpiresAtUtc);

/// <summary>委托创建响应。</summary>
public sealed record CreateAiAgentDelegationResponse(Guid DelegationId);

/// <summary>撤销委托请求。</summary>
public sealed record RevokeAiAgentDelegationRequest(long ExpectedVersion);

/// <summary>委托可读摘要。</summary>
public sealed record AiAgentDelegationResponse(
    Guid Id,
    Guid GrantorUserId,
    Guid GranteeUserId,
    string? ToolName,
    string? PermissionCode,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    long Version,
    DateTimeOffset CreatedAtUtc);
