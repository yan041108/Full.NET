namespace Full.NET.Modules.Identity.Contracts;

/// <summary>字段投影资源的稳定语义键。</summary>
public static class FieldProjectionResourceKeys
{
    /// <summary>Host 用户列表、详情与导出资源。</summary>
    public const string HostUsers = "identity.host_users";
}

/// <summary>角色字段投影授权管理权限。</summary>
public static class IdentityRoleFieldGrantPermissions
{
    /// <summary>读取字段目录与角色字段授权。</summary>
    public const string Read = "identity.role_field_grants.read";

    /// <summary>替换角色字段授权。</summary>
    public const string Replace = "identity.role_field_grants.replace";

    /// <summary>迁移 056 前遗留的粗粒度写权限；不再进入可分配目录。</summary>
    public const string Write = "identity.role_field_grants.write";
}

/// <summary>字段信息泄露后的风险级别。</summary>
public enum FieldProjectionSensitivity
{
    /// <summary>普通业务字段。</summary>
    Public = 0,

    /// <summary>仅供内部管理使用的字段。</summary>
    Internal = 1,

    /// <summary>需要角色显式授权的安全敏感字段。</summary>
    Sensitive = 2,
}

/// <summary>字段在没有显式角色授权时的可见性。</summary>
public enum FieldProjectionDefaultVisibility
{
    /// <summary>为兼容和资源识别而始终返回。</summary>
    Mandatory = 0,

    /// <summary>没有显式授权时不得读取或返回。</summary>
    Restricted = 1,
}

/// <summary>稳定字段目录项；不包含物理表名、列名或 SQL 片段。</summary>
/// <param name="FieldKey">稳定字段键；在同一资源内唯一。</param>
/// <param name="DisplayName">面向管理员展示的中文名称。</param>
/// <param name="Sensitivity">字段泄露风险级别。</param>
/// <param name="DefaultVisibility">未显式授权时的默认可见性策略。</param>
/// <param name="Assignable">是否允许在角色字段授权页被显式分配；Mandatory 字段始终为 false。</param>
public sealed record FieldProjectionFieldDefinition(
    string FieldKey,
    string DisplayName,
    FieldProjectionSensitivity Sensitivity,
    FieldProjectionDefaultVisibility DefaultVisibility,
    bool Assignable);

/// <summary>稳定资源目录项。</summary>
/// <param name="ResourceKey">稳定资源键；全局唯一。</param>
/// <param name="DisplayName">面向管理员展示的中文资源名称。</param>
/// <param name="Fields">该资源下已发布的字段定义集合。</param>
public sealed record FieldProjectionResourceDefinition(
    string ResourceKey,
    string DisplayName,
    IReadOnlyList<FieldProjectionFieldDefinition> Fields);

/// <summary>当前用户访问资源时的服务端有效字段集合。</summary>
/// <param name="ResourceKey">目标资源键。</param>
/// <param name="FieldKeys">实际生效的字段键；顺序与目录一致，便于列表按列渲染。</param>
public sealed record UserFieldProjection(
    string ResourceKey,
    IReadOnlyList<string> FieldKeys);

/// <summary>替换角色在一个资源上的显式字段授权。</summary>
/// <param name="ResourceKey">目标资源键。</param>
/// <param name="FieldKeys">提交后应完整生效的显式字段键；必须是目录中 Assignable 为 true 的字段。</param>
/// <param name="Version">调用方看到的当前版本；服务端据此拒绝并发覆盖。</param>
public sealed record ReplaceHostRoleFieldGrantsRequest(
    string ResourceKey,
    IReadOnlyList<string> FieldKeys,
    int Version);

/// <summary>角色在一个资源上的显式字段授权。</summary>
/// <param name="RoleId">目标角色标识。</param>
/// <param name="ResourceKey">目标资源键。</param>
/// <param name="FieldKeys">当前显式授权的字段键集合；不含 Mandatory 隐含字段。</param>
/// <param name="Version">乐观并发版本。</param>
public sealed record HostRoleFieldGrantsResponse(
    Guid RoleId,
    string ResourceKey,
    IReadOnlyList<string> FieldKeys,
    int Version);

/// <summary>解析用户在目标资源上的有效字段。</summary>
public interface IUserFieldProjectionResolver
{
    /// <summary>
    /// 返回目录约束后的有序字段集合；Host 资源要求 <paramref name="tenantId"/> 为 null。
    /// </summary>
    /// <param name="userId">待解析的用户标识。</param>
    /// <param name="tenantId">目标租户标识；Host 作用域资源必须为 <see langword="null"/>。</param>
    /// <param name="resourceKey">稳定资源键；必须来自已发布的字段投影资源目录。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>按目录顺序排列的实际生效字段集合。</returns>
    Task<UserFieldProjection> ResolveAsync(
        Guid userId,
        Guid? tenantId,
        string resourceKey,
        CancellationToken cancellationToken = default);
}
