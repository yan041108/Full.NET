namespace Full.NET.Modules.Identity.Persistence;

/// <summary>访问令牌校验专用投影；只读取实时撤销、账号状态与作用域判断所需字段。</summary>
internal sealed class AccessSessionValidationRecord
{
    public Guid UserId { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public Guid? ActiveTenantId { get; set; }
    public Guid? TenantId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset? LockoutEndUtc { get; set; }
    public string SecurityStamp { get; set; } = string.Empty;
}
