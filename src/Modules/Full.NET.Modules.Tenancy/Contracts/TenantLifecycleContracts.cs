namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>租户生命周期状态迁移的权限码字符串常量。</summary>
/// <remarks>
/// 权限码字符串发布后不可改名或删除；新增权限只能追加到本类末尾。状态迁移必须遵循状态机定义，Close 为不可逆终态。
/// </remarks>
public static class TenancyTenantLifecyclePermissions
{
    /// <summary>读取租户生命周期状态与迁移记录；面向管理后台与租户解析链路。</summary>
    public const string Read = "tenancy.tenant_lifecycle.read";

    /// <summary>暂停租户；暂停后租户不可登录、不可解析，但数据保留可恢复。</summary>
    public const string Suspend = "tenancy.tenant_lifecycle.suspend";

    /// <summary>恢复已暂停的租户；恢复后租户重新进入 Active 状态。</summary>
    public const string Reactivate = "tenancy.tenant_lifecycle.reactivate";

    /// <summary>关闭租户；进入 Closing 中间态后转入 Closed 终态，不可逆。</summary>
    public const string Close = "tenancy.tenant_lifecycle.close";

    /// <summary>转移租户所有权；用于关闭前移交或组织变更。</summary>
    public const string TransferOwnership = "tenancy.tenant_lifecycle.transfer_ownership";
}

/// <summary>租户生命周期状态常量；描述租户的业务状态机阶段。</summary>
/// <remarks>
/// 状态字符串值发布后不可改名或删除；新增状态只能追加到本类末尾。状态迁移必须遵循状态机：Active 与 Suspended 之间可逆迁移，Closing 为不可逆中间态，Closed 为终态。
/// </remarks>
public static class TenantLifecycleStatuses
{
    /// <summary>活动状态；租户可正常登录、解析与消费资源。</summary>
    public const string Active = "Active";

    /// <summary>暂停状态；租户不可登录、不可解析，但数据保留可恢复。</summary>
    public const string Suspended = "Suspended";

    /// <summary>关闭中状态；进入清理流程的不可逆中间态，最终转入 Closed。</summary>
    public const string Closing = "Closing";

    /// <summary>已关闭状态；终态，租户数据按保留策略归档，不再接受任何写操作。</summary>
    public const string Closed = "Closed";
}

/// <summary>租户开通状态常量；描述开通流程的进度阶段。</summary>
/// <remarks>
/// 状态字符串值发布后不可改名或删除；新增状态只能追加到本类末尾。Failed 后须通过补偿流程重试，不自动回滚已写入步骤。
/// </remarks>
public static class TenantProvisioningStatuses
{
    /// <summary>待处理；开通请求已受理尚未开始执行。</summary>
    public const string Pending = "Pending";

    /// <summary>进行中；开通流程正在执行某个步骤。</summary>
    public const string InProgress = "InProgress";

    /// <summary>已完成；开通流程成功结束，租户进入 Active 状态。</summary>
    public const string Completed = "Completed";

    /// <summary>失败；开通流程在某个步骤出错，须通过补偿流程重试。</summary>
    public const string Failed = "Failed";
}

/// <summary>租户开通步骤常量；描述开通流程内部各阶段。</summary>
/// <remarks>
/// 步骤字符串值发布后不可改名或删除；新增步骤只能追加到本类末尾。步骤顺序由实现方决定，调用方不应假设固定顺序。
/// </remarks>
public static class TenantProvisioningSteps
{
    /// <summary>创建租户记录；写入 Identifier、Name、Domain 等不可变字段。</summary>
    public const string CreatingTenant = "CreatingTenant";

    /// <summary>绑定所有者；将初始所有者用户与租户关联。</summary>
    public const string BindingOwner = "BindingOwner";

    /// <summary>激活租户；将租户状态置为 Active，开通完成。</summary>
    public const string Activating = "Activating";
}

/// <summary>暂停租户的请求；调用方须回传当前 Version 以做 CAS 守卫。</summary>
/// <remarks>
/// 暂停是可逆操作：Suspended 状态的租户可通过 Reactivate 恢复。迁移必须幂等，对已 Suspended 的租户再次暂停视为成功。
/// </remarks>
/// <param name="Version">调用方看到的当前版本；服务端据此拒绝并发覆盖。</param>
public sealed record SuspendTenantRequest(int Version);

/// <summary>恢复已暂停租户的请求；调用方须回传当前 Version 以做 CAS 守卫。</summary>
/// <remarks>
/// 恢复是幂等操作：对 Active 状态的租户再次恢复视为成功。仅 Suspended 状态可恢复，Closed 与 Closing 不可恢复。
/// </remarks>
/// <param name="Version">调用方看到的当前版本；服务端据此拒绝并发覆盖。</param>
public sealed record ReactivateTenantRequest(int Version);

/// <summary>关闭租户的请求；进入 Closing 中间态后转入 Closed 终态，不可逆。</summary>
/// <remarks>
/// Close 为不可逆终态迁移；调用方须先确认无活动订阅与未结清账单。迁移幂等：对 Closing 或 Closed 状态再次调用视为成功。最后一名保护：关闭租户前若仅剩一名所有者，须先转移所有权或确认保留策略。
/// </remarks>
/// <param name="Version">调用方看到的当前版本；服务端据此拒绝并发覆盖。</param>
public sealed record CloseTenantRequest(int Version);

/// <summary>转移租户所有权的请求；将所有者变更为指定用户。</summary>
/// <remarks>
/// 转移须确保新所有者已存在且属于当前 Host 作用域；转移后原所有者失去管理权。最后一名保护：关闭租户前若仅剩一名所有者，须先转移或确认保留策略。
/// </remarks>
/// <param name="NewOwnerUserId">新所有者的用户标识；须为已存在且未被禁用的用户。</param>
/// <param name="Version">调用方看到的当前版本；服务端据此拒绝并发覆盖。</param>
public sealed record TransferTenantOwnershipRequest(Guid NewOwnerUserId, int Version);