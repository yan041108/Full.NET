namespace Full.NET.Modules.Reporting.Contracts;

/// <summary>受支持的数据库提供程序键。</summary>
public static class ReportingDataSourceProviderKeys
{
    /// <summary>SQL Server 提供程序。</summary>
    public const string SqlServer = "sql_server";

    /// <summary>MySQL 提供程序。</summary>
    public const string MySql = "mysql";
}

/// <summary>数据源连接测试状态键。</summary>
public static class ReportingDataSourceTestStatusKeys
{
    /// <summary>最近一次测试成功。</summary>
    public const string Succeeded = "succeeded";

    /// <summary>最近一次测试失败。</summary>
    public const string Failed = "failed";
}

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>报表数据源列表项；连接信息已脱敏，不包含凭据。</summary>
/// <param name="Id">数据源稳定标识。</param>
/// <param name="TenantId">所属租户标识；为空表示 Host 级配置。</param>
/// <param name="Name">显示名称。</param>
/// <param name="ProviderKey">数据库提供程序键。</param>
/// <param name="MaskedServerEndpoint">脱敏后的服务器端点（主机与端口）。</param>
/// <param name="MaskedDatabaseName">脱敏后的数据库名。</param>
/// <param name="MaskedUsername">脱敏后的只读账号。</param>
/// <param name="HasPassword">是否已配置密码。</param>
/// <param name="TrustServerCertificate">是否信任服务器证书（仅 SQL Server）。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="LastTestedAtUtc">最近一次连接测试时间（UTC）。</param>
/// <param name="LastTestStatusKey">最近一次连接测试状态键。</param>
/// <param name="LastTestMessage">最近一次连接测试摘要。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record ReportingDataSourceListItem(
    Guid Id,
    Guid? TenantId,
    string Name,
    string ProviderKey,
    string MaskedServerEndpoint,
    string MaskedDatabaseName,
    string MaskedUsername,
    bool HasPassword,
    bool TrustServerCertificate,
    bool IsEnabled,
    DateTimeOffset? LastTestedAtUtc,
    string? LastTestStatusKey,
    string? LastTestMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>报表数据源详情；不回显密码或连接串。</summary>
/// <param name="Id">数据源稳定标识。</param>
/// <param name="TenantId">所属租户标识；为空表示 Host 级配置。</param>
/// <param name="Name">显示名称。</param>
/// <param name="ProviderKey">数据库提供程序键。</param>
/// <param name="ServerHost">数据库服务器主机名。</param>
/// <param name="Port">数据库端口。</param>
/// <param name="DatabaseName">数据库名称。</param>
/// <param name="Username">只读账号用户名。</param>
/// <param name="HasPassword">是否已配置密码。</param>
/// <param name="TrustServerCertificate">是否信任服务器证书（仅 SQL Server）。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="LastTestedAtUtc">最近一次连接测试时间（UTC）。</param>
/// <param name="LastTestStatusKey">最近一次连接测试状态键。</param>
/// <param name="LastTestMessage">最近一次连接测试摘要。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record ReportingDataSourceResponse(
    Guid Id,
    Guid? TenantId,
    string Name,
    string ProviderKey,
    string ServerHost,
    int Port,
    string DatabaseName,
    string Username,
    bool HasPassword,
    bool TrustServerCertificate,
    bool IsEnabled,
    DateTimeOffset? LastTestedAtUtc,
    string? LastTestStatusKey,
    string? LastTestMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>创建报表数据源请求。</summary>
/// <param name="TenantId">所属租户标识；为空表示 Host 级配置。</param>
/// <param name="Name">显示名称。</param>
/// <param name="ProviderKey">数据库提供程序键。</param>
/// <param name="ServerHost">数据库服务器主机名。</param>
/// <param name="Port">数据库端口。</param>
/// <param name="DatabaseName">数据库名称。</param>
/// <param name="Username">只读账号用户名。</param>
/// <param name="Password">只读账号密码；仅写入时接受，响应不回显。</param>
/// <param name="TrustServerCertificate">是否信任服务器证书（仅 SQL Server）。</param>
/// <param name="IsEnabled">是否启用。</param>
public sealed record CreateReportingDataSourceRequest(
    Guid? TenantId,
    string Name,
    string ProviderKey,
    string ServerHost,
    int Port,
    string DatabaseName,
    string Username,
    string Password,
    bool TrustServerCertificate,
    bool IsEnabled);

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>更新报表数据源请求。</summary>
/// <param name="Name">显示名称。</param>
/// <param name="ProviderKey">数据库提供程序键。</param>
/// <param name="ServerHost">数据库服务器主机名。</param>
/// <param name="Port">数据库端口。</param>
/// <param name="DatabaseName">数据库名称。</param>
/// <param name="Username">只读账号用户名。</param>
/// <param name="Password">可选的新密码；为空表示保留现有密码。</param>
/// <param name="TrustServerCertificate">是否信任服务器证书（仅 SQL Server）。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="Version">客户端感知的乐观并发版本号。</param>
public sealed record UpdateReportingDataSourceRequest(
    string Name,
    string ProviderKey,
    string ServerHost,
    int Port,
    string DatabaseName,
    string Username,
    string? Password,
    bool TrustServerCertificate,
    bool IsEnabled,
    int Version);

/// <summary>报表数据源连接测试结果。</summary>
/// <param name="Succeeded">是否成功。</param>
/// <param name="Message">诊断消息；失败时包含原因摘要。</param>
public sealed record TestReportingDataSourceResult(
    bool Succeeded,
    string Message);
