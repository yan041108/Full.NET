using Full.NET.Localization;

namespace Full.NET.Modules.Tenancy.Domain;

/// <summary>
/// 租户聚合根（不可变记录）。状态机不变量：
/// - 任何时刻至少存在 1 名活动租户；Disable 操作会在 CountActiveTenants 降到 1 时拒绝。
/// - Domain 全局唯一，用于未认证场景下的域名解析；冲突由开通流程做唯一校验。
/// - Version 字段用于 HostTenantManagementService.Update/Disable 场景的乐观并发，
///   并发更新冲突时返回 VersionConflict 由客户端重读后重试。
/// - 开通/禁用产生的缓存失效由写入方事务提交后立即触发 TenantCacheInvalidator。
/// </summary>
/// <param name="Id">租户唯一 ID。</param>
/// <param name="Identifier">租户短标识，供 API/CLI 做可读引用。</param>
/// <param name="Name">展示名称。</param>
/// <param name="Domain">绑定的访问域名（唯一）；用于未登录时按域解析。</param>
/// <param name="IsActive">是否启用；false 时现有会话与解析全部拒绝进入。</param>
/// <param name="CreatedAtUtc">创建时间 UTC。</param>
/// <param name="Version">乐观并发版本号；每次 UPDATE +1。</param>
/// <param name="DefaultLocale">该租户默认界面语言。</param>
internal sealed record Tenant(
    Guid Id,
    string Identifier,
    string Name,
    string Domain,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    int Version,
    string DefaultLocale = LocaleCatalog.DefaultLocale);
