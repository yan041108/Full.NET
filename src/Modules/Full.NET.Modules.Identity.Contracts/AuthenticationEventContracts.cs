namespace Full.NET.Modules.Identity.Contracts;

/// <summary>平台认证事件查询权限。</summary>
/// <remarks>权限码字符串发布后不可改名或删除；新增权限只能追加，已发布权限码不得调整顺序。</remarks>
public static class AuthenticationEventPermissions
{
    /// <summary>分页查询认证事件列表与详情。</summary>
    public const string Read = "identity.authentication_events.read";

    /// <summary>导出认证事件审计归档。</summary>
    public const string Export = "identity.authentication_events.export";
}

/// <summary>认证事件的安全投影；不返回用户名指纹、IP、User-Agent 或任何凭据。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">认证事件稳定标识。</param>
/// <param name="UserId">触发该事件的用户标识；系统级事件为 <see langword="null"/>。</param>
/// <param name="SessionId">关联会话标识；无会话上下文时为 <see langword="null"/>。</param>
/// <param name="EventType">稳定事件类型键（如 Login、Logout）；发布后不可改名。</param>
/// <param name="ResultCode">稳定结果码；用于区分成功与各类失败原因。</param>
/// <param name="Succeeded">本次认证事件是否成功。</param>
/// <param name="ContextTenantId">事件发生时的上下文租户标识；无租户上下文时为 <see langword="null"/>。</param>
/// <param name="OccurredAtUtc">事件发生时间（UTC）。</param>
/// <param name="ActorUserId">触发该事件的操作人用户标识；系统自动事件为 <see langword="null"/>。</param>
/// <param name="TraceId">关联的分布式追踪标识。</param>
/// <param name="AuthenticationMethod">认证方式键（如 Password、Sso）；用于审计归类。</param>
/// <param name="ClientId">发起认证的客户端标识。</param>
/// <param name="CenterSessionId">中心会话标识；跨应用会话同步时使用。</param>
/// <param name="ApplicationSessionId">应用会话标识；单应用登录态追踪时使用。</param>
public sealed record AuthenticationEventResponse(
    Guid Id,
    Guid? UserId,
    Guid? SessionId,
    string EventType,
    string ResultCode,
    bool Succeeded,
    Guid? ContextTenantId,
    DateTimeOffset OccurredAtUtc,
    Guid? ActorUserId,
    string? TraceId,
    string? AuthenticationMethod,
    string? ClientId,
    Guid? CenterSessionId,
    Guid? ApplicationSessionId);

/// <summary>按时间和标识稳定翻页的认证事件结果。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Items">当前页认证事件列表；顺序按发生时间倒序。</param>
/// <param name="NextCursor">下一页游标；为 <see langword="null"/> 表示已到末尾。</param>
public sealed record AuthenticationEventCursorPage(
    IReadOnlyList<AuthenticationEventResponse> Items,
    string? NextCursor);
