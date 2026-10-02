namespace Full.NET.Modules.Ai.Contracts;

/// <summary>创建持久委托；至少限定工具名或权限码之一。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。ToolName 与 PermissionCode 不得同时为空。</remarks>
/// <param name="GranteeUserId">被授予委托的用户标识。</param>
/// <param name="ToolName">限定可被代理调用的工具名；与 <paramref name="PermissionCode"/> 至少其一非空。</param>
/// <param name="PermissionCode">限定可被代理使用的权限码；与 <paramref name="ToolName"/> 至少其一非空。</param>
/// <param name="ExpiresAtUtc">委托失效时间（UTC）；到期后自动失效，不可续期。</param>
public sealed record CreateAiAgentDelegationRequest(
    Guid GranteeUserId,
    string? ToolName,
    string? PermissionCode,
    DateTimeOffset ExpiresAtUtc);

/// <summary>委托创建响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="DelegationId">新建委托的稳定标识。</param>
public sealed record CreateAiAgentDelegationResponse(Guid DelegationId);

/// <summary>撤销委托请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="ExpectedVersion">委托的预期版本号；用于 CAS 乐观并发，防止撤销过期视图。</param>
public sealed record RevokeAiAgentDelegationRequest(long ExpectedVersion);

/// <summary>委托可读摘要。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">委托稳定标识。</param>
/// <param name="GrantorUserId">委托授予人用户标识。</param>
/// <param name="GranteeUserId">被授予委托的用户标识。</param>
/// <param name="ToolName">限定可被代理调用的工具名；按权限码委托时为 <see langword="null"/>。</param>
/// <param name="PermissionCode">限定可被代理使用的权限码；按工具委托时为 <see langword="null"/>。</param>
/// <param name="ExpiresAtUtc">委托失效时间（UTC）。</param>
/// <param name="RevokedAtUtc">委托撤销时间（UTC）；未撤销时为 <see langword="null"/>。</param>
/// <param name="Version">委托当前版本号；每次状态变更递增，用于并发控制。</param>
/// <param name="CreatedAtUtc">委托创建时间（UTC）。</param>
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
