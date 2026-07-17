namespace Full.NET.Modules.Identity.Persistence;

/// <summary>
/// 当前用户资料查询只加载公开字段，避免在普通资料接口中接触密码哈希。
/// </summary>
internal sealed class IdentityProfileRecord
{
    public Guid Id { get; set; }

    public string ScopeKey { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public string PreferredLocale { get; set; } = string.Empty;

    public int ProfileVersion { get; set; }
}
