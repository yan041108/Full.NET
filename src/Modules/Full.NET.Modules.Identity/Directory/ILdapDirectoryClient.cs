namespace Full.NET.Modules.Identity.Directory;

/// <summary>LDAP 目录运行时连接参数；仅供内部客户端使用。</summary>
/// <param name="Host">LDAP 主机名。</param>
/// <param name="Port">LDAP 端口。</param>
/// <param name="UseTls">是否启用 TLS。</param>
/// <param name="BaseDn">目录根 DN。</param>
/// <param name="BindDn">服务账号绑定 DN。</param>
/// <param name="BindPassword">明文服务账号密码。</param>
/// <param name="UserSearchFilter">用户搜索过滤器模板。</param>
/// <param name="UserAccountAttribute">用户账号属性名。</param>
/// <param name="EmployeeIdAttribute">工号属性名。</param>
/// <param name="DepartmentCodeAttribute">部门编码属性名。</param>
/// <param name="SyncSearchBaseDn">同步预览白名单搜索根 DN。</param>
internal sealed record LdapConnectionRuntimeSettings(
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
    string SyncSearchBaseDn);

/// <summary>LDAP 连接测试 outcome。</summary>
/// <param name="Succeeded">是否成功。</param>
/// <param name="Message">诊断消息。</param>
internal sealed record LdapConnectionTestOutcome(
    bool Succeeded,
    string Message);

/// <summary>LDAP 用户认证测试 outcome。</summary>
/// <param name="Succeeded">是否成功。</param>
/// <param name="MatchedDn">匹配到的用户 DN。</param>
/// <param name="Message">诊断消息。</param>
internal sealed record LdapAuthenticationTestOutcome(
    bool Succeeded,
    string? MatchedDn,
    string Message);

/// <summary>LDAP 目录条目快照。</summary>
/// <param name="Dn">目录 DN。</param>
/// <param name="EntryKind">条目类型。</param>
/// <param name="Account">用户账号。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="Mail">邮箱。</param>
/// <param name="DepartmentCode">部门编码。</param>
internal sealed record LdapDirectoryEntrySnapshot(
    string Dn,
    string EntryKind,
    string? Account,
    string? DisplayName,
    string? Mail,
    string? DepartmentCode);

/// <summary>可测试的 LDAP 目录客户端抽象。</summary>
internal interface ILdapDirectoryClient
{
    /// <summary>测试服务账号连接与绑定。</summary>
    /// <param name="settings">运行时连接参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>测试结果。</returns>
    Task<LdapConnectionTestOutcome> TestConnectionAsync(
        LdapConnectionRuntimeSettings settings,
        CancellationToken cancellationToken = default);

    /// <summary>搜索用户并以该用户凭据尝试绑定。</summary>
    /// <param name="settings">运行时连接参数。</param>
    /// <param name="account">用户账号。</param>
    /// <param name="password">用户密码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>认证测试结果。</returns>
    Task<LdapAuthenticationTestOutcome> TestAuthenticationAsync(
        LdapConnectionRuntimeSettings settings,
        string account,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>在允许的搜索根下预览目录条目。</summary>
    /// <param name="settings">运行时连接参数。</param>
    /// <param name="searchBaseDn">搜索根 DN。</param>
    /// <param name="maxEntries">返回条目上限。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>目录条目快照列表。</returns>
    Task<IReadOnlyList<LdapDirectoryEntrySnapshot>> PreviewEntriesAsync(
        LdapConnectionRuntimeSettings settings,
        string searchBaseDn,
        int maxEntries,
        CancellationToken cancellationToken = default);
}
