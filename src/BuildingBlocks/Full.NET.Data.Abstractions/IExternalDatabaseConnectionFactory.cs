namespace Full.NET.Data.Abstractions;

/// <summary>
/// 打开调用方指定的外部数据库会话。只用于报表等受控数据源，禁止把应用主库连接串交给业务模块拼接。
/// 外部 MySQL 不得套用 Full.NET 主库 Guid 存储策略。
/// </summary>
public interface IExternalDatabaseConnectionFactory
{
    /// <summary>打开外部会话；失败时不抛出驱动异常，返回不含驱动详情的固定摘要。</summary>
    /// <param name="request">已由业务模块校验过的连接参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时携带已打开会话，调用方负责释放。</returns>
    Task<ExternalDatabaseSessionResult> OpenAsync(
        ExternalDatabaseConnectionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>已打开的外部数据库会话；查询与释放都通过本接口完成，业务模块不接触具体驱动。</summary>
public interface IExternalDatabaseSession : IAsyncDisposable
{
    /// <summary>执行标量探活 SQL。</summary>
    /// <param name="sql">受审查 SQL 文本。</param>
    /// <param name="commandTimeoutSeconds">命令超时秒数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>标量结果或失败摘要。</returns>
    Task<ExternalDatabaseScalarResult> ExecuteScalarAsync(
        string sql,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken = default);

    /// <summary>执行查询并读取全部单元格文本；单元格超长时截断。</summary>
    /// <param name="sql">受审查 SQL 文本。</param>
    /// <param name="parameters">参数名到值；空值按数据库空值写入。</param>
    /// <param name="commandTimeoutSeconds">命令超时秒数。</param>
    /// <param name="maxCellValueLength">单个单元格最大字符数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>列名、行数据或失败摘要。</returns>
    Task<ExternalDatabaseQueryResult> ExecuteQueryAsync(
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        int commandTimeoutSeconds,
        int maxCellValueLength,
        CancellationToken cancellationToken = default);
}

/// <summary>外部数据库连接请求；密码仅在内存中传递，不得写入日志。</summary>
/// <param name="Provider">目标提供程序。</param>
/// <param name="ServerHost">服务器主机名或地址。</param>
/// <param name="Port">端口。</param>
/// <param name="DatabaseName">数据库名。</param>
/// <param name="Username">登录名。</param>
/// <param name="Password">明文密码。</param>
/// <param name="TrustServerCertificate">SQL Server 是否信任服务器证书。</param>
/// <param name="ConnectionTimeoutSeconds">连接超时秒数。</param>
/// <param name="ApplicationName">驱动报告的应用程序名。</param>
public sealed record ExternalDatabaseConnectionRequest(
    DatabaseProvider Provider,
    string ServerHost,
    int Port,
    string DatabaseName,
    string Username,
    string Password,
    bool TrustServerCertificate,
    int ConnectionTimeoutSeconds,
    string ApplicationName);

/// <summary>外部会话打开结果。</summary>
/// <param name="Succeeded">是否打开成功。</param>
/// <param name="ErrorMessage">失败摘要；成功时为空。</param>
/// <param name="Session">已打开会话；失败时为空，成功时由调用方释放。</param>
public sealed record ExternalDatabaseSessionResult(
    bool Succeeded,
    string? ErrorMessage,
    IExternalDatabaseSession? Session);

/// <summary>外部标量执行结果。</summary>
/// <param name="Succeeded">是否执行成功。</param>
/// <param name="ErrorMessage">失败摘要；成功时为空。</param>
/// <param name="Value">标量值；无结果时为空。</param>
public sealed record ExternalDatabaseScalarResult(
    bool Succeeded,
    string? ErrorMessage,
    object? Value);

/// <summary>外部查询执行结果。</summary>
/// <param name="Succeeded">是否执行成功。</param>
/// <param name="ErrorMessage">失败摘要；成功时为空。</param>
/// <param name="ColumnKeys">结果列名。</param>
/// <param name="Rows">单元格文本行。</param>
public sealed record ExternalDatabaseQueryResult(
    bool Succeeded,
    string? ErrorMessage,
    IReadOnlyList<string> ColumnKeys,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows);
