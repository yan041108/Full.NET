namespace Full.NET.Modules.Identity.Domain;

/// <summary>
/// 认证相关审计事件记录。包含登录/刷新/登出/超管变更等事件。
/// 安全设计：UsernameFingerprint 保存归一化用户名的 SHA-256 指纹而不是明文，
/// 避免数据库备份泄漏完整账号列表；需要对特定用户做检索时由调用方现场 Compute
/// 相同指纹做查询。IpAddress/UserAgent 入库前会被截断至安全长度。
/// </summary>
/// <param name="Id">事件唯一 ID。</param>
/// <param name="UserId">关联用户；失败登录且用户未知时为 null。</param>
/// <param name="SessionId">关联 Refresh Session；未建立会话前为 null。</param>
/// <param name="UsernameFingerprint">归一化用户名的 SHA-256 十六进制指纹。</param>
/// <param name="EventType">事件分类：login/refresh/logout/super_admin.* 等。</param>
/// <param name="ResultCode">Identity Error Code 或内部稳定结果代码。</param>
/// <param name="Succeeded">事件是否成功。</param>
/// <param name="IpAddress">来源 IP；最大 64 字符，兼容 IPv6。</param>
/// <param name="UserAgent">来源 User-Agent 摘要；最大 512 字符。</param>
/// <param name="ContextTenantId">事件发生时所在的活动租户上下文。</param>
/// <param name="OccurredAtUtc">事件发生时间 UTC。</param>
internal sealed record AuthAuditEvent(
    Guid Id,
    Guid? UserId,
    Guid? SessionId,
    string UsernameFingerprint,
    string EventType,
    string ResultCode,
    bool Succeeded,
    string? IpAddress,
    string? UserAgent,
    Guid? ContextTenantId,
    DateTimeOffset OccurredAtUtc);
