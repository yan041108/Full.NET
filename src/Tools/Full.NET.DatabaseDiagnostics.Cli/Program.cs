using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using Full.NET.DatabaseDiagnostics.Cli;
using Microsoft.Data.SqlClient;
using MySqlConnector;

// 独立运维进程只运行固定元数据 SELECT；不进入业务 Host，也不接受任意 SQL。
Console.OutputEncoding = System.Text.Encoding.UTF8;
var results = new List<object>();
string? provider = null;
string check = "connection";
string? table = null;
string? connectionEnvironment = null;
try
{
    var seen = new HashSet<string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i += 2)
    {
        if (i + 1 >= args.Length || !seen.Add(args[i]))
            throw new ArgumentException();
        switch (args[i])
        {
            case "--provider": provider = args[i + 1]; break;
            case "--check": check = args[i + 1]; break;
            case "--table": table = args[i + 1]; break;
            case "--connection-env": connectionEnvironment = args[i + 1]; break;
            default: throw new ArgumentException();
        }
    }
    if (provider is not ("SqlServer" or "MySql") ||
        check is not ("connection" or "catalog" or "indexes" or "blocking" or "all") ||
        (table is not null && !Regex.IsMatch(table, "^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)) ||
        (connectionEnvironment is not null && !Regex.IsMatch(connectionEnvironment, "^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant)))
        throw new ArgumentException();
}
catch (ArgumentException)
{
    WriteReport(false, "invalid_arguments");
    return 2;
}

try
{
    var environmentName = connectionEnvironment ?? (provider == "SqlServer"
        ? "FULLNET_DIAGNOSTICS_SQLSERVER_CONNECTION"
        : "FULLNET_DIAGNOSTICS_MYSQL_CONNECTION");
    var connectionString = Environment.GetEnvironmentVariable(environmentName);
    // 配置只从显式诊断变量或本工具自己的 User Secrets 读取，不自动借用生产/迁移账户。
    if (connectionEnvironment is null && string.IsNullOrWhiteSpace(connectionString))
    {
        var secretsPath = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "UserSecrets", "fullnet-database-diagnostics", "secrets.json")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".microsoft", "usersecrets", "fullnet-database-diagnostics", "secrets.json");
        if (File.Exists(secretsPath))
        {
            using var secrets = JsonDocument.Parse(await File.ReadAllTextAsync(secretsPath));
            if (secrets.RootElement.TryGetProperty($"ConnectionStrings:{provider}", out var value))
                connectionString = value.GetString();
        }
    }
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        WriteReport(false, "connection_not_configured");
        return 2;
    }

    await using DbConnection connection = CreateConnection(provider!, connectionString);
    using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    await connection.OpenAsync(budget.Token);
    var checks = check == "all" ? new[] { "connection", "catalog", "indexes", "blocking" } : [check];
    var success = true;
    foreach (var name in checks)
    {
        try
        {
            // Performance Schema 关闭时系统表可能为空，必须先区分不可用与无阻塞。
            if (provider == "MySql" && name == "blocking")
            {
                await using var availability = connection.CreateCommand();
                availability.CommandText = "SELECT @@performance_schema;";
                availability.CommandTimeout = 8;
                using var availabilityBudget = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
                availabilityBudget.CancelAfter(TimeSpan.FromSeconds(10));
                if (Convert.ToInt32(await availability.ExecuteScalarAsync(availabilityBudget.Token)) != 1)
                {
                    success = false;
                    results.Add(new { check = name, ok = false, error = new { category = "performance_schema_disabled" } });
                    continue;
                }
            }
            await using var command = connection.CreateCommand();
            command.CommandText = DiagnosticQueries.Get(provider!, name);
            command.CommandTimeout = 8;
            if (name is "catalog" or "indexes")
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@TableName";
                parameter.DbType = DbType.String;
                parameter.Size = 64;
                parameter.Value = (object?)table ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }
            using var queryBudget = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
            queryBudget.CancelAfter(TimeSpan.FromSeconds(10));
            await using var reader = await command.ExecuteReaderAsync(queryBudget.Token);
            var rows = new List<Dictionary<string, object?>>();
            var truncated = false;
            while (await reader.ReadAsync(queryBudget.Token))
            {
                if (rows.Count == 100) { truncated = true; break; }
                var row = new Dictionary<string, object?>();
                for (var i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }
            results.Add(new { check = name, ok = true, truncated, rows });
        }
        catch (Exception exception) when (exception is DbException or OperationCanceledException)
        {
            success = false;
            results.Add(new { check = name, ok = false, error = Classify(exception) });
        }
    }
    WriteReport(success, null);
    return success ? 0 : 1;
}
catch (Exception exception)
{
    // 禁止输出异常 Message、InnerException 或原连接串，保留稳定分类和驱动数字码。
    WriteReport(false, Classify(exception));
    return 1;
}

void WriteReport(bool ok, object? error) => Console.WriteLine(JsonSerializer.Serialize(new
{
    provider, check, table, ok, error, capturedAtUtc = DateTimeOffset.UtcNow,
    visibility = "permissions-dependent; empty metadata is not proof of absence",
    results
}, new JsonSerializerOptions { WriteIndented = true }));

static DbConnection CreateConnection(string provider, string value)
{
    if (provider == "SqlServer")
    {
        var options = new SqlConnectionStringBuilder(value)
        {
            ConnectTimeout = 5, ConnectRetryCount = 0, Pooling = false,
            ApplicationName = "Full.NET.DatabaseDiagnostics"
        };
        if (string.IsNullOrWhiteSpace(options.InitialCatalog)) throw new ArgumentException();
        return new SqlConnection(options.ConnectionString);
    }
    var mysql = new MySqlConnectionStringBuilder(value)
    {
        ConnectionTimeout = 5, Pooling = false, AllowLoadLocalInfile = false,
        AllowUserVariables = false, GuidFormat = MySqlGuidFormat.Binary16,
        ApplicationName = "Full.NET.DatabaseDiagnostics"
    };
    if (string.IsNullOrWhiteSpace(mysql.Database)) throw new ArgumentException();
    return new MySqlConnection(mysql.ConnectionString);
}

static object Classify(Exception exception)
{
    int? number = exception switch { SqlException sql => sql.Number, MySqlException mysql => mysql.Number, _ => null };
    var category = exception switch
    {
        OperationCanceledException => "timeout_or_cancelled",
        SqlException { Number: 18456 } or MySqlException { Number: 1045 } => "authentication_failed",
        SqlException { Number: 229 or 297 or 300 or 50001 } or
            MySqlException { Number: 1044 or 1142 or 1227 } => "permission_denied",
        SqlException { Number: 4060 } or MySqlException { Number: 1049 } => "database_unavailable",
        SqlException { Number: -2 } or MySqlException { Number: 3024 } => "timeout_or_cancelled",
        ArgumentException or JsonException or InvalidOperationException => "invalid_configuration",
        IOException or UnauthorizedAccessException => "configuration_unavailable",
        DbException => "database_error",
        _ => "diagnostic_failed"
    };
    return new { category, driverCode = number };
}
