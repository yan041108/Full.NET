namespace Full.NET.Modules.Identity.Contracts;

/// <summary>权威确认当前交互会话与权限，不接受调用方提供的用户或租户覆盖。</summary>
public interface ICurrentSessionAuthorization
{
    /// <summary>账号、会话、租户或权限失效时返回空；不支持持久委托或 API Key 代替会话。</summary>
    /// <param name="permissionCode">所需的精确权限码；为空时仅校验会话有效性。</param>
    /// <param name="cancellationToken">用于取消授权校验的令牌。</param>
    /// <returns>授权成功的最小主体快照；任一条件不满足时为 <see langword="null"/>。</returns>
    Task<AuthorizedSessionActor?> AuthorizeAsync(string permissionCode, CancellationToken cancellationToken = default);
}

/// <summary>权威授权后可供业务模块使用的最小主体快照。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="UserId">当前会话所属用户的稳定标识。</param>
/// <param name="TenantId">当前会话绑定的租户标识；Host 级会话为 <see langword="null"/>。</param>
/// <param name="SessionId">当前交互会话标识，用于审计与会话失效联动。</param>
public sealed record AuthorizedSessionActor(Guid UserId, Guid? TenantId, Guid SessionId);
