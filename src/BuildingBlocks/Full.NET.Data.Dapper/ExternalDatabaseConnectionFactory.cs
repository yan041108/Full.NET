using System.Data.Common;
using System.Globalization;
using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.Data.Dapper;

/// <summary>
/// 按受控参数打开外部 SQL Server / MySQL 会话。SQL Server 强制加密；
/// 外部 MySQL 不套用主库 Binary16 Guid 策略，避免破坏客户库类型约定。
/// </summary>
internal sealed class ExternalDatabaseConnectionFactory : IExternalDatabaseConnectionFactory
{
    /// <inheritdoc />
    public async Task<ExternalDatabaseSessionResult> OpenAsync(
        ExternalDatabaseConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        DbConnection? connection = null;
        try
        {
            connection = request.Provider switch
            {
                DatabaseProvider.SqlServer => CreateSqlServerConnection(request),
                DatabaseProvider.MySql => CreateMySqlConnection(request),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(request.Provider),
                    request.Provider,
                    "Unsupported external database provider."),
            };
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return new ExternalDatabaseSessionResult(
                true,
                null,
                new ExternalDatabaseSession(connection));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            return new ExternalDatabaseSessionResult(false, SanitizeMessage(exception.Message), null);
        }
    }

    /// <summary>构造强制加密的 SQL Server 连接，尚未打开。</summary>
    /// <param name="request">外部连接请求。</param>
    private static SqlConnection CreateSqlServerConnection(ExternalDatabaseConnectionRequest request)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"{request.ServerHost},{request.Port}",
            InitialCatalog = request.DatabaseName,
            UserID = request.Username,
            Password = request.Password,
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = request.TrustServerCertificate,
            ConnectTimeout = request.ConnectionTimeoutSeconds,
            ApplicationName = request.ApplicationName,
        };
        return new SqlConnection(builder.ConnectionString);
    }

    /// <summary>构造外部 MySQL 连接，不改写客户库 Guid 存储格式。</summary>
    /// <param name="request">外部连接请求。</param>
    private static MySqlConnection CreateMySqlConnection(ExternalDatabaseConnectionRequest request)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = request.ServerHost,
            Port = (uint)request.Port,
            Database = request.DatabaseName,
            UserID = request.Username,
            Password = request.Password,
            ConnectionTimeout = (uint)request.ConnectionTimeoutSeconds,
            ApplicationName = request.ApplicationName,
        };
        return new MySqlConnection(builder.ConnectionString);
    }

    /// <summary>截断驱动错误，避免把连接串或过长堆栈泄漏给调用方。</summary>
    /// <param name="message">原始异常消息。</param>
    private static string SanitizeMessage(string message) =>
        message.Length <= 512 ? message : message[..512];
}

/// <summary>封装已打开的外部连接，把命令执行留在数据边界内。</summary>
/// <param name="connection">已打开连接。</param>
internal sealed class ExternalDatabaseSession(DbConnection connection) : IExternalDatabaseSession
{
    /// <inheritdoc />
    public async Task<ExternalDatabaseScalarResult> ExecuteScalarAsync(
        string sql,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = commandTimeoutSeconds;
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return new ExternalDatabaseScalarResult(true, null, value);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ExternalDatabaseScalarResult(false, SanitizeMessage(exception.Message), null);
        }
    }

    /// <inheritdoc />
    public async Task<ExternalDatabaseQueryResult> ExecuteQueryAsync(
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        int commandTimeoutSeconds,
        int maxCellValueLength,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = commandTimeoutSeconds;
            foreach (var (name, value) in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name.StartsWith("@", StringComparison.Ordinal) ? name : $"@{name}";
                parameter.Value = value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var columnKeys = Enumerable.Range(0, reader.FieldCount)
                .Select(reader.GetName)
                .ToArray();
            var rows = new List<IReadOnlyDictionary<string, string?>>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var row = new Dictionary<string, string?>(StringComparer.Ordinal);
                for (var index = 0; index < columnKeys.Length; index++)
                {
                    row[columnKeys[index]] = FormatCellValue(reader, index, maxCellValueLength);
                }

                rows.Add(row);
            }

            return new ExternalDatabaseQueryResult(true, null, columnKeys, rows);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or TimeoutException)
        {
            return new ExternalDatabaseQueryResult(false, SanitizeMessage(exception.Message), [], []);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => connection.DisposeAsync();

    /// <summary>把单元格格式化为不变区域性文本并截断。</summary>
    /// <param name="reader">结果读取器。</param>
    /// <param name="ordinal">列序号。</param>
    /// <param name="maxCellValueLength">最大字符数。</param>
    private static string? FormatCellValue(DbDataReader reader, int ordinal, int maxCellValueLength)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        var text = value switch
        {
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
        if (text is null)
        {
            return null;
        }

        return text.Length <= maxCellValueLength ? text : text[..maxCellValueLength];
    }

    /// <summary>截断驱动错误。</summary>
    /// <param name="message">原始异常消息。</param>
    private static string SanitizeMessage(string message) =>
        message.Length <= 512 ? message : message[..512];
}
