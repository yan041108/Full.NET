namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>租户权益（Feature/Limit）目录与绑定的权限码字符串常量。</summary>
/// <remarks>
/// 权限码字符串发布后不可改名或删除；新增权限只能追加到本类末尾，避免破坏既有角色分配与策略缓存。
/// </remarks>
public static class TenancyTenantEntitlementPermissions
{
    /// <summary>读取租户权益目录与绑定；面向管理后台、订阅履行与配额执行链路。</summary>
    public const string Read = "tenancy.tenant_entitlements.read";

    /// <summary>维护权益目录条目（Feature/Limit）的元数据与版本。</summary>
    public const string ManageCatalog = "tenancy.tenant_entitlements.manage_catalog";

    /// <summary>维护租户与权益之间的绑定关系；影响权益实际生效范围。</summary>
    public const string ManageBindings = "tenancy.tenant_entitlements.manage_bindings";

    /// <summary>切换权益执行阶段（Compatibility/Shadow/Enforced）；影响限流与拒绝行为。</summary>
    public const string ManageEnforcement = "tenancy.tenant_entitlements.manage_enforcement";

    /// <summary>对未绑定权益的活跃租户执行兼容回填 dry-run/apply。</summary>
    public const string ReconcileBackfill = "tenancy.tenant_entitlements.reconcile_backfill";
}

/// <summary>权益类型常量；区分功能开关与额度限制两类语义。</summary>
/// <remarks>
/// 字符串值发布后不可改名或删除；新增类型只能追加到本类末尾，以保证既有目录与绑定记录的稳定性。
/// </remarks>
public static class TenantEntitlementTypes
{
    /// <summary>功能开关类权益；用于开启/关闭特定能力，不带数量约束。</summary>
    public const string Feature = "Feature";

    /// <summary>额度限制类权益；用于声明上限，需配合配额模块执行。</summary>
    public const string Limit = "Limit";
}

/// <summary>权益执行阶段常量；用于灰度推进限流策略。</summary>
/// <remarks>
/// 阶段字符串值发布后不可改名或删除；新增阶段只能追加到本类末尾。阶段语义参见执行模块文档。
/// </remarks>
public static class TenantEntitlementEnforcementPhases
{
    /// <summary>兼容阶段；不阻断调用，仅记录未满足权益的请求。</summary>
    public const string Compatibility = "Compatibility";

    /// <summary>影子阶段；与生产执行一致但不向调用方暴露拒绝结果，用于验证真实流量下的命中情况。</summary>
    public const string Shadow = "Shadow";

    /// <summary>强制阶段；未满足权益的请求按目录配置直接拒绝。</summary>
    public const string Enforced = "Enforced";
}

/// <summary>租户权益目录条目的只读响应；描述单个 Feature 或 Limit 的元数据。</summary>
/// <remarks>
/// 字段顺序发布后不可重排；新增字段只能追加到主构造函数末尾，以保证既有客户端反序列化稳定性。
/// </remarks>
/// <param name="Id">权益目录条目的稳定标识。</param>
/// <param name="Code">权益编码；在 Host 作用域内唯一且创建后不可变。</param>
/// <param name="Name">权益显示名称。</param>
/// <param name="Description">权益说明文本；可省略。</param>
/// <param name="EntitlementType">权益类型；取值见 <see cref="TenantEntitlementTypes"/>。</param>
/// <param name="IsActive">是否处于活动状态；非活动条目不可再绑定到租户。</param>
/// <param name="Version">乐观并发版本；写操作须回传以避免覆盖并发变更。</param>
public sealed record TenantEntitlementCatalogResponse(
    Guid Id, string Code, string Name, string? Description,
    string EntitlementType, bool IsActive, int Version);

/// <summary>租户与权益目录条目之间绑定关系的只读响应；描述权益在租户上的生效窗口。</summary>
/// <remarks>
/// 字段顺序发布后不可重排；新增字段只能追加到末尾。生效窗口的时间边界使用 UTC，调用方须自行转换显示时区。
/// </remarks>
/// <param name="Id">绑定关系的稳定标识。</param>
/// <param name="TenantId">目标租户标识。</param>
/// <param name="EntitlementId">被绑定的权益目录条目标识。</param>
/// <param name="EntitlementCode">权益编码；冗余展示字段，与 EntitlementId 对应。</param>
/// <param name="EntitlementName">权益显示名称；冗余展示字段，用于减少目录联表查询。</param>
/// <param name="EffectiveFromUtc">生效起始时间（UTC）；早于此时间的请求视为未绑定。</param>
/// <param name="EffectiveToUtc">生效结束时间（UTC）；<see langword="null"/> 表示长期有效。</param>
/// <param name="SourcePackageId">触发该绑定的套餐标识；<see langword="null"/> 表示由手工绑定。</param>
/// <param name="Version">乐观并发版本；写操作须回传以避免覆盖并发变更。</param>
public sealed record TenantEntitlementBindingResponse(
    Guid Id, Guid TenantId, Guid EntitlementId, string EntitlementCode,
    string EntitlementName, DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc, Guid? SourcePackageId, int Version);

/// <summary>创建租户权益目录条目的请求；用于声明一个 Feature 或 Limit。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="Code">权益编码；须在 Host 作用域内保持唯一，创建后不可变。</param>
/// <param name="Name">权益显示名称。</param>
/// <param name="Description">权益说明文本；可省略。</param>
/// <param name="EntitlementType">权益类型；须为 <see cref="TenantEntitlementTypes"/> 中的已知值。</param>
public sealed record CreateTenantEntitlementCatalogRequest(
    string Code, string Name, string? Description, string EntitlementType);

/// <summary>为指定租户绑定权益目录条目的请求；绑定后权益在生效窗口内对租户生效。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="EntitlementId">被绑定的权益目录条目标识。</param>
/// <param name="EffectiveFromUtc">生效起始时间（UTC）；调用方须使用 UTC，避免跨时区漂移。</param>
/// <param name="EffectiveToUtc">生效结束时间（UTC）；<see langword="null"/> 表示长期有效。</param>
/// <param name="SourcePackageId">触发该绑定的套餐标识；<see langword="null"/> 表示由手工绑定。</param>
public sealed record CreateTenantEntitlementBindingRequest(
    Guid EntitlementId, DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc, Guid? SourcePackageId);

/// <summary>权益执行阶段的只读响应；用于查询当前阶段与并发版本。</summary>
/// <remarks>
/// 字段顺序发布后不可重排；新增字段只能追加到末尾。Phase 取值须为 <see cref="TenantEntitlementEnforcementPhases"/> 中的已知值。
/// </remarks>
/// <param name="Phase">当前执行阶段；取值见 <see cref="TenantEntitlementEnforcementPhases"/>。</param>
/// <param name="Version">乐观并发版本；用于下一次更新请求回传。</param>
public sealed record TenantEntitlementEnforcementResponse(string Phase, int Version);

/// <summary>更新权益执行阶段的请求；调用方须回传最近读取的 Version 以做 CAS 守卫。</summary>
/// <remarks>
/// 切换到 Enforced 阶段会立即对未满足权益的请求生效，调用方须先确认 Shadow 阶段无关键失败再切换。
/// </remarks>
/// <param name="Phase">目标执行阶段；取值见 <see cref="TenantEntitlementEnforcementPhases"/>。</param>
/// <param name="Version">调用方看到的当前版本；服务端据此拒绝并发覆盖。</param>
public sealed record UpdateTenantEntitlementEnforcementRequest(string Phase, int Version);

/// <summary>权益兼容回填结果。</summary>
public sealed record TenantEntitlementBackfillResponse(
    int MissingBindingCount,
    int AppliedCount,
    bool DryRun,
    IReadOnlyList<Guid> TenantIds);

/// <summary>权益兼容回填请求。</summary>
public sealed record TenantEntitlementBackfillRequest(bool DryRun = true);