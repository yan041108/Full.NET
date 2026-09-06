namespace Full.NET.Modules.Identity.Contracts;

/// <summary>LDAP 连接管理权限码。</summary>
public static class IdentityLdapConnectionPermissions
{
    /// <summary>分页查询 LDAP 连接。</summary>
    public const string Read = "identity.ldap_connections.read";

    /// <summary>创建 LDAP 连接。</summary>
    public const string Create = "identity.ldap_connections.create";

    /// <summary>更新 LDAP 连接。</summary>
    public const string Update = "identity.ldap_connections.update";

    /// <summary>删除 LDAP 连接。</summary>
    public const string Delete = "identity.ldap_connections.delete";

    /// <summary>测试 LDAP 连接与服务账号绑定。</summary>
    public const string Test = "identity.ldap_connections.test";

    /// <summary>预览 LDAP 目录同步条目（只读）。</summary>
    public const string PreviewSync = "identity.ldap_connections.preview_sync";
}

/// <summary>LDAP 连接响应；不包含凭据字段。</summary>
/// <param name="Id">连接稳定标识。</param>
/// <param name="TenantId">所属租户标识；为空表示 Host 级配置。</param>
/// <param name="Name">显示名称。</param>
/// <param name="Host">LDAP 主机名。</param>
/// <param name="Port">LDAP 端口。</param>
/// <param name="UseTls">是否启用 TLS。</param>
/// <param name="BaseDn">目录根 DN。</param>
/// <param name="BindDn">服务账号绑定 DN。</param>
/// <param name="UserSearchFilter">用户搜索过滤器模板。</param>
/// <param name="UserAccountAttribute">用户账号属性名。</param>
/// <param name="EmployeeIdAttribute">工号属性名。</param>
/// <param name="DepartmentCodeAttribute">部门编码属性名。</param>
/// <param name="SyncSearchBaseDn">同步预览允许的搜索根 DN。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）；未更新时为 <see langword="null"/>。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record LdapConnectionResponse(
    Guid Id,
    Guid? TenantId,
    string Name,
    string Host,
    int Port,
    bool UseTls,
    string BaseDn,
    string BindDn,
    string UserSearchFilter,
    string UserAccountAttribute,
    string? EmployeeIdAttribute,
    string? DepartmentCodeAttribute,
    string SyncSearchBaseDn,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>创建 LDAP 连接请求。</summary>
/// <param name="TenantId">所属租户标识；为空表示 Host 级配置。</param>
/// <param name="Name">显示名称。</param>
/// <param name="Host">LDAP 主机名。</param>
/// <param name="Port">LDAP 端口。</param>
/// <param name="UseTls">是否启用 TLS。</param>
/// <param name="BaseDn">目录根 DN。</param>
/// <param name="BindDn">服务账号绑定 DN。</param>
/// <param name="BindPassword">服务账号密码；仅写入时接受，响应不回显。</param>
/// <param name="UserSearchFilter">用户搜索过滤器模板。</param>
/// <param name="UserAccountAttribute">用户账号属性名。</param>
/// <param name="EmployeeIdAttribute">工号属性名。</param>
/// <param name="DepartmentCodeAttribute">部门编码属性名。</param>
/// <param name="SyncSearchBaseDn">同步预览允许的搜索根 DN。</param>
/// <param name="IsEnabled">是否启用。</param>
public sealed record CreateLdapConnectionRequest(
    Guid? TenantId,
    string Name,
    string Host,
    int Port,
    bool UseTls,
    string BaseDn,
    string BindDn,
    string BindPassword,
    string UserSearchFilter,
    string UserAccountAttribute,
    string? EmployeeIdAttribute,
    string? DepartmentCodeAttribute,
    string SyncSearchBaseDn,
    bool IsEnabled);

/// <summary>更新 LDAP 连接请求。</summary>
/// <param name="Name">显示名称。</param>
/// <param name="Host">LDAP 主机名。</param>
/// <param name="Port">LDAP 端口。</param>
/// <param name="UseTls">是否启用 TLS。</param>
/// <param name="BaseDn">目录根 DN。</param>
/// <param name="BindDn">服务账号绑定 DN。</param>
/// <param name="BindPassword">可选的新服务账号密码；为空表示保留现有密码。</param>
/// <param name="UserSearchFilter">用户搜索过滤器模板。</param>
/// <param name="UserAccountAttribute">用户账号属性名。</param>
/// <param name="EmployeeIdAttribute">工号属性名。</param>
/// <param name="DepartmentCodeAttribute">部门编码属性名。</param>
/// <param name="SyncSearchBaseDn">同步预览允许的搜索根 DN。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="Version">客户端感知的乐观并发版本号。</param>
public sealed record UpdateLdapConnectionRequest(
    string Name,
    string Host,
    int Port,
    bool UseTls,
    string BaseDn,
    string BindDn,
    string? BindPassword,
    string UserSearchFilter,
    string UserAccountAttribute,
    string? EmployeeIdAttribute,
    string? DepartmentCodeAttribute,
    string SyncSearchBaseDn,
    bool IsEnabled,
    int Version);

/// <summary>LDAP 连接测试结果。</summary>
/// <param name="Succeeded">是否成功。</param>
/// <param name="Message">诊断消息；失败时包含原因摘要。</param>
public sealed record TestLdapConnectionResult(
    bool Succeeded,
    string Message);

/// <summary>LDAP 用户认证测试请求。</summary>
/// <param name="Account">待验证的用户账号。</param>
/// <param name="Password">待验证的用户密码。</param>
public sealed record TestLdapAuthenticationRequest(
    string Account,
    string Password);

/// <summary>LDAP 用户认证测试结果。</summary>
/// <param name="Succeeded">是否成功以搜索到的用户 DN 完成绑定。</param>
/// <param name="MatchedDn">匹配到的目录 DN；失败时为 <see langword="null"/>。</param>
/// <param name="Message">诊断消息。</param>
public sealed record TestLdapAuthenticationResult(
    bool Succeeded,
    string? MatchedDn,
    string Message);

/// <summary>LDAP 同步预览请求。</summary>
/// <param name="SearchBaseDn">可选的搜索根 DN；默认使用连接配置的 <see cref="LdapConnectionResponse.SyncSearchBaseDn"/>。</param>
/// <param name="MaxEntries">返回条目上限；服务端会裁剪到允许范围。</param>
public sealed record PreviewLdapSyncRequest(
    string? SearchBaseDn,
    int? MaxEntries);

/// <summary>LDAP 同步预览条目类型。</summary>
public static class LdapSyncPreviewEntryKinds
{
    /// <summary>用户条目。</summary>
    public const string User = "user";

    /// <summary>组织单元条目。</summary>
    public const string OrganizationalUnit = "organizationalUnit";
}

/// <summary>LDAP 同步预览条目。</summary>
/// <param name="Dn">目录 DN。</param>
/// <param name="EntryKind">条目类型：<see cref="LdapSyncPreviewEntryKinds.User"/> 或 <see cref="LdapSyncPreviewEntryKinds.OrganizationalUnit"/>。</param>
/// <param name="Account">用户账号；组织单元为空。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="Mail">邮箱。</param>
/// <param name="DepartmentCode">部门编码。</param>
public sealed record LdapSyncPreviewEntry(
    string Dn,
    string EntryKind,
    string? Account,
    string? DisplayName,
    string? Mail,
    string? DepartmentCode);

/// <summary>LDAP 同步预览响应。</summary>
/// <param name="SearchBaseDn">实际使用的搜索根 DN。</param>
/// <param name="Entries">预览条目列表。</param>
public sealed record PreviewLdapSyncResponse(
    string SearchBaseDn,
    IReadOnlyList<LdapSyncPreviewEntry> Entries);
