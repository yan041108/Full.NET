using Full.NET.Localization;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Domain;

/// <summary>
/// 用户聚合根（不可变记录）。按 ScopeKey + NormalizedUsername 做唯一约束。
/// 并发保护依赖 Version 字段与乐观更新 SQL；SecurityStamp 变化时
/// 当前 RefreshSession 会被视为无效从而强制重新登录（用于超管变更、改密、停用）。
/// </summary>
/// <param name="Id">用户唯一 ID。</param>
/// <param name="TenantId">租户作用域用户所属租户；Host 用户为 null。</param>
/// <param name="ScopeKey">作用域键："host" 或 "tenant:{tenantId:N}"。</param>
/// <param name="Username">登录用户名，保留原始大小写。</param>
/// <param name="NormalizedUsername">全大写归一化用户名，用于唯一索引与防大小写碰撞登录。</param>
/// <param name="DisplayName">展示名。</param>
/// <param name="PasswordHash">PBKDF2 输出；从不写入明文。</param>
/// <param name="IsActive">是否启用；false 时登录与 Refresh 全部拒绝。</param>
/// <param name="FailedLoginCount">连续失败登录计数；成功时清零。</param>
/// <param name="LockoutEndUtc">达到阈值后的锁定到期时间（UTC）。</param>
/// <param name="SecurityStamp">安全戳；变更后全部现存 JWT/Session 失效。</param>
/// <param name="CreatedAtUtc">创建时间。</param>
/// <param name="UpdatedAtUtc">最近更新时间。</param>
/// <param name="Version">乐观并发版本号；每次 UPDATE +1。</param>
/// <param name="PreferredLocale">用户偏好界面语言。</param>
/// <param name="ProfileVersion">扩展资料版本号。</param>
/// <param name="AccountType">账号类型：普通用户或外部同步账号。</param>
internal sealed record IdentityUser(
    Guid Id,
    Guid? TenantId,
    string ScopeKey,
    string Username,
    string NormalizedUsername,
    string DisplayName,
    string PasswordHash,
    bool IsActive,
    int FailedLoginCount,
    DateTimeOffset? LockoutEndUtc,
    string SecurityStamp,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version,
    string PreferredLocale = LocaleCatalog.DefaultLocale,
    int ProfileVersion = 1,
    string AccountType = IdentityAccountTypes.NormalUser);
