using System.Data.Common;
using System.Globalization;
using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.Data.Dapper;

/// <summary>
/// 按受控参数打开外部 SQL Server / MySQL 会话。SQL Server 强制加密；
/// 外部 MySQL 不套用主库 Binary16 Guid 策略，避免破坏客户库类型约定。
/// </summary>
internal sealed class ExternalDatabaseConnectionFactory(
    IOptions<ExternalDatabaseAccessOptions> accessOptions) : IExternalDatabaseConnectionFactory
{
    private readonly SemaphoreSlim _sessionGate = new(accessOptions.Value.MaxConcurrentSessions);

    /// <inheritdoc />
    public async Task<ExternalDatabaseSessionResult> OpenAsync(
        ExternalDatabaseConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!ExternalDatabaseAccessPolicy.IsAllowed(request, accessOptions.Value))
        {
            return new ExternalDatabaseSessionResult(false, "External database destination is not allowed.", null);
        }

        if (!await _sessionGate.WaitAsync(
                TimeSpan.FromSeconds(accessOptions.Value.AdmissionTimeoutSeconds),
                cancellationToken).ConfigureAwait(false))
        {
            return new ExternalDatabaseSessionResult(false, "External database capacity is busy.", null);
        }

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
                new ExternalDatabaseSession(connection, _sessionGate));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            try
            {
                await DisposeFailedConnectionAsync(connection).ConfigureAwait(false);
            }
            finally
            {
                _sessionGate.Release();
            }
            return new ExternalDatabaseSessionResult(false, "Failed to open external database connection.", null);
        }
        catch (OperationCanceledException)
        {
            try
            {
                await DisposeFailedConnectionAsync(connection).ConfigureAwait(false);
            }
            finally
            {
                _sessionGate.Release();
            }

            throw;
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
    internal static MySqlConnection CreateMySqlConnection(ExternalDatabaseConnectionRequest request)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = request.ServerHost,
            Port = (uint)request.Port,
            Database = request.DatabaseName,
            UserID = request.Username,
            Password = request.Password,
            SslMode = MySqlSslMode.VerifyFull,
            ConnectionTimeout = (uint)request.ConnectionTimeoutSeconds,
            ApplicationName = request.ApplicationName,
        };
        return new MySqlConnection(builder.ConnectionString);
    }

    private static async ValueTask DisposeFailedConnectionAsync(DbConnection? connection)
    {
        if (connection is null)
        {
            return;
        }

        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 清理异常不得覆盖已脱敏的连接失败摘要，也不得阻止配额释放。
        }
    }
}

/// <summary>封装已打开的外部连接，把命令执行留在数据边界内。</summary>
/// <param name="connection">已打开连接。</param>
internal sealed class ExternalDatabaseSession(DbConnection connection, SemaphoreSlim sessionGate) : IExternalDatabaseSession
{
    private int _disposed;

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
            return new ExternalDatabaseScalarResult(false, "Failed to probe external database.", null);
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
            return new ExternalDatabaseQueryResult(false, "External database query failed.", [], []);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Failed to close external database session.");
        }
        finally
        {
            sessionGate.Release();
        }
    }

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
}
