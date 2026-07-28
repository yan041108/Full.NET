using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Testcontainers.MsSql;
using Testcontainers.MySql;

namespace Full.NET.Benchmarks.Auditing;

public sealed record AuditingQueryPageResult(
    long TotalRows,
    int ReturnedRows,
    string OrderedRowIdsSignature);

internal abstract class AuditingBenchmarkDatabase : IAsyncDisposable
{
    private const string Password = "FullNet_Benchmark!123";
    private const int SeedBatchSize = 10_000;

    protected AuditingBenchmarkDatabase(string connectionString)
    {
        ConnectionString = connectionString;
    }

    public abstract string ProviderName { get; }

    public abstract string ContainerImage { get; }

    public abstract string PlanFileExtension { get; }

    protected string ConnectionString { get; }

    public static async Task<AuditingBenchmarkDatabase> StartAsync(
        string provider,
        CancellationToken cancellationToken)
    {
        AuditingBenchmarkDatabase database = provider switch
        {
            "sqlserver" => await SqlServerAuditingBenchmarkDatabase.StartContainerAsync(
                Password,
                cancellationToken),
            "mysql" => await MySqlAuditingBenchmarkDatabase.StartContainerAsync(
                Password,
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "不支持的基准数据库 Provider。"),
        };

        try
        {
            await database.MigrateAsync(cancellationToken);
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public abstract Task SeedAsync(
        int rows,
        DateTimeOffset referenceUtc,
        CancellationToken cancellationToken);

    public abstract Task<AuditingQueryPageResult> ExecutePageAsync(
        AuditingQueryScenario scenario,
        CancellationToken cancellationToken);

    public abstract Task<IReadOnlyDictionary<string, string>> CapturePlansAsync(
        AuditingQueryScenario scenario,
        CancellationToken cancellationToken);

    public abstract Task<string> GetVersionAsync(CancellationToken cancellationToken);

    public abstract ValueTask DisposeAsync();

    protected static DataTable CreateSeedTable(
        int startIndex,
        int count,
        int totalRows,
        DateTimeOffset referenceUtc,
        bool binaryGuids,
        bool dateTimeOffset)
    {
        var table = new DataTable
        {
            Locale = CultureInfo.InvariantCulture,
        };
        table.Columns.Add("Id", binaryGuids ? typeof(byte[]) : typeof(Guid));
        table.Columns.Add(
            "OccurredAtUtc",
            dateTimeOffset ? typeof(DateTimeOffset) : typeof(DateTime));
        table.Columns.Add("HttpMethod", typeof(string));
        table.Columns.Add("RequestPath", typeof(string));
        table.Columns.Add("StatusCode", typeof(int));
        table.Columns.Add("DurationMs", typeof(int));
        table.Columns.Add("UserId", binaryGuids ? typeof(byte[]) : typeof(Guid));
        table.Columns.Add("TenantId", binaryGuids ? typeof(byte[]) : typeof(Guid));
        table.Columns.Add("TraceId", typeof(string));
        table.Columns.Add("ClientIpFingerprint", typeof(string));
        table.Columns.Add("IsAuthenticated", typeof(bool));

        var datasetStart = referenceUtc.AddDays(-30);
        var denominator = Math.Max(1, totalRows - 1);
        for (var offset = 0; offset < count; offset++)
        {
            var index = startIndex + offset;
            var progress = (double)index / denominator;
            var occurredAtUtc = datasetStart.AddTicks(
                (long)((referenceUtc - datasetStart).Ticks * progress));
            var id = Guid.CreateVersion7(occurredAtUtc);
            var row = table.NewRow();
            row["Id"] = binaryGuids ? id.ToByteArray(bigEndian: true) : id;
            if (dateTimeOffset)
            {
                row["OccurredAtUtc"] = occurredAtUtc;
            }
            else
            {
                row["OccurredAtUtc"] = occurredAtUtc.UtcDateTime;
            }
            row["HttpMethod"] = index % 5 == 0 ? "POST" : "GET";
            row["RequestPath"] = index % 10 == 0
                ? $"{AuditingQueryScenarios.MatchingPath}/items/{index % 1000}"
                : $"/api/v1/tenants/{index % 1000}/summary";
            row["StatusCode"] = index % 20 == 0 ? 500 : 200;
            row["DurationMs"] = 5 + (index % 250);
            row["UserId"] = DBNull.Value;
            row["TenantId"] = DBNull.Value;
            row["TraceId"] = $"benchmark-{index:D12}";
            row["ClientIpFingerprint"] =
                $"benchmark-client-{index % 100:D3}";
            row["IsAuthenticated"] = index % 5 != 0;
            table.Rows.Add(row);
        }

        return table;
    }

    protected static object CreateParameters(AuditingQueryScenario scenario) =>
        new
        {
            scenario.FromUtc,
            scenario.ToUtc,
            scenario.HttpMethod,
            scenario.StatusCode,
            scenario.PathContains,
            scenario.Offset,
            scenario.PageSize,
        };

    protected static AuditingQueryPageResult CreatePageResult(
        long totalRows,
        IReadOnlyList<dynamic> rows)
    {
        var orderedRowIdsSignature = string.Join(
            "|",
            rows.Select(row => FormatRowId((object)row.Id)));
        return new AuditingQueryPageResult(
            totalRows,
            rows.Count,
            orderedRowIdsSignature);
    }

    protected static async Task MigrateAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken)
    {
        var result = await new DbUpMigrationRunner(
                Options.Create(new DatabaseOptions
                {
                    Provider = provider,
                    ConnectionString = connectionString,
                    MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
                    CommandTimeoutSeconds = 120,
                }),
                NullLoggerFactory.Instance,
                Options.Create(new UuidBinaryContractOptions
                {
                    MaintenanceMode = true,
                    BackupVerified = true,
                    LegacyWritersStopped = true,
                    DestructiveDdlApprovalId = "benchmark-uuid-contract",
                }),
                Options.Create(new PreV1NamingContractOptions
                {
                    MaintenanceMode = true,
                    BackupVerified = true,
                    LegacyWritersStopped = true,
                    LegacyOutboxDrained = true,
                    DestructiveDdlApprovalId = "benchmark-naming-contract",
                }))
            .MigrateAsync(cancellationToken);
        if (!result.Successful)
        {
            throw new InvalidOperationException("基准数据库迁移失败。");
        }
    }

    protected static IEnumerable<(int Start, int Count)> SeedBatches(int rows)
    {
        for (var start = 0; start < rows; start += SeedBatchSize)
        {
            yield return (start, Math.Min(SeedBatchSize, rows - start));
        }
    }

    private Task MigrateAsync(CancellationToken cancellationToken) =>
        MigrateAsync(
            ProviderName == "sqlserver"
                ? DatabaseProvider.SqlServer
                : DatabaseProvider.MySql,
            ConnectionString,
            cancellationToken);

    private static string FormatRowId(object value) =>
        value switch
        {
            Guid guid => guid.ToString("N"),
            byte[] bytes => Convert.ToHexString(bytes),
            ReadOnlyMemory<byte> bytes => Convert.ToHexString(bytes.Span),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
                ?? throw new InvalidOperationException("基准查询返回了空行标识。"),
        };
}

internal sealed class SqlServerAuditingBenchmarkDatabase(
    MsSqlContainer container,
    string connectionString)
    : AuditingBenchmarkDatabase(connectionString), IAuditingCursorBenchmarkDatabase
{
    public const string Image = "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04";

    public override string ProviderName => "sqlserver";

    public override string ContainerImage => Image;

    public override string PlanFileExtension => "showplan.xml";

    public static async Task<SqlServerAuditingBenchmarkDatabase> StartContainerAsync(
        string password,
        CancellationToken cancellationToken)
    {
        var container = new MsSqlBuilder(Image)
            .WithPassword(password)
            .Build();
        await container.StartAsync(cancellationToken);
        try
        {
            var baseConnectionString = container.GetConnectionString();
            await using (var connection = new SqlConnection(baseConnectionString))
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        "CREATE DATABASE [fullnet_benchmark];",
                        cancellationToken: cancellationToken));
            }

            var connectionString = new SqlConnectionStringBuilder(baseConnectionString)
            {
                InitialCatalog = "fullnet_benchmark",
            }.ConnectionString;
            return new SqlServerAuditingBenchmarkDatabase(container, connectionString);
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }

    public override async Task SeedAsync(
        int rows,
        DateTimeOffset referenceUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        foreach (var (start, count) in SeedBatches(rows))
        {
            using var table = CreateSeedTable(
                start,
                count,
                rows,
                referenceUtc,
                binaryGuids: false,
                dateTimeOffset: true);
            using var bulkCopy = new SqlBulkCopy(
                connection,
                SqlBulkCopyOptions.TableLock,
                externalTransaction: null)
            {
                DestinationTableName = "dbo.fn_auditing_access_log",
                BatchSize = count,
                BulkCopyTimeout = 120,
            };
            foreach (DataColumn column in table.Columns)
            {
                bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            }

            await bulkCopy.WriteToServerAsync(table, cancellationToken);
        }
    }

    public override async Task<AuditingQueryPageResult> ExecutePageAsync(
        AuditingQueryScenario scenario,
        CancellationToken cancellationToken) =>
        await ExecuteSqlServerPageAsync(
            AuditingSqlServerQueryStrategy.CurrentOptional,
            scenario,
            cancellationToken);

    public async Task<AuditingQueryPageResult> ExecuteSqlServerPageAsync(
        AuditingSqlServerQueryStrategy strategy,
        AuditingQueryScenario scenario,
        CancellationToken cancellationToken)
    {
        var query = AuditingSqlServerQueryFactory.Create(strategy, scenario);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(
                query.PageSql,
                CreateParameters(scenario),
                commandTimeout: 120,
                cancellationToken: cancellationToken));
        var total = await grid.ReadSingleAsync<long>();
        var rows = (await grid.ReadAsync()).ToList();
        return CreatePageResult(total, rows);
    }

    public override async Task<IReadOnlyDictionary<string, string>> CapturePlansAsync(
        AuditingQueryScenario scenario,
        CancellationToken cancellationToken) =>
        await CaptureSqlServerPlansAsync(
            AuditingSqlServerQueryStrategy.CurrentOptional,
            scenario,
            cancellationToken);

    public async Task<IReadOnlyDictionary<string, string>> CaptureSqlServerPlansAsync(
        AuditingSqlServerQueryStrategy strategy,
        AuditingQueryScenario scenario,
        CancellationToken cancellationToken)
    {
        var query = AuditingSqlServerQueryFactory.Create(strategy, scenario);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            new CommandDefinition(
                "SET STATISTICS XML ON",
                cancellationToken: cancellationToken));
        try
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["count"] = await CapturePlanAsync(
                    connection,
                    query.CountSql,
                    scenario,
                    cancellationToken),
                ["list"] = await CapturePlanAsync(
                    connection,
                    query.ListSql,
                    scenario,
                    cancellationToken),
            };
        }
        finally
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "SET STATISTICS XML OFF",
                    cancellationToken: cancellationToken));
        }
    }

    public async Task ClearPlanCacheAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            new CommandDefinition(
                "DBCC FREEPROCCACHE",
                commandTimeout: 120,
                cancellationToken: cancellationToken));
    }

    public async Task<AuditingCursorBoundary> FindDeepCursorBoundaryAsync(
        int offset,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var record = await connection.QuerySingleAsync<SqlServerCursorBoundaryRecord>(
            new CommandDefinition(
                """
                SELECT Id, OccurredAtUtc
                FROM fn_auditing_access_log
                ORDER BY OccurredAtUtc DESC, Id DESC
                OFFSET @BoundaryOffset ROWS FETCH NEXT 1 ROWS ONLY
                """,
                new { BoundaryOffset = offset - 1 },
                commandTimeout: 120,
                cancellationToken: cancellationToken));
        return new AuditingCursorBoundary(record.OccurredAtUtc, record.Id);
    }

    public async Task<AuditingQueryPageResult> ExecuteCursorComparisonAsync(
        AuditingCursorQueryStrategy strategy,
        AuditingCursorBoundary boundary,
        int offset,
        int pageSize,
        int totalRows,
        CancellationToken cancellationToken)
    {
        var query = AuditingCursorQueryFactory.Create(ProviderName, strategy);
        var parameters = CreateCursorParameters(boundary, offset, pageSize);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        if (query.CountSql is not null)
        {
            await using var grid = await connection.QueryMultipleAsync(
                new CommandDefinition(
                    $"{query.CountSql.TrimEnd()};{Environment.NewLine}{query.ListSql}",
                    parameters,
                    commandTimeout: 120,
                    cancellationToken: cancellationToken));
            var count = await grid.ReadSingleAsync<long>();
            var offsetRows = (await grid.ReadAsync()).ToList();
            return CreatePageResult(count, offsetRows);
        }

        var cursorRows = (await connection.QueryAsync(
            new CommandDefinition(
                query.ListSql,
                parameters,
                commandTimeout: 120,
                cancellationToken: cancellationToken))).ToList();
        return CreatePageResult(totalRows, cursorRows);
    }

    public async Task<IReadOnlyDictionary<string, string>>
        CaptureCursorComparisonPlansAsync(
            AuditingCursorQueryStrategy strategy,
            AuditingCursorBoundary boundary,
            int offset,
            int pageSize,
            CancellationToken cancellationToken)
    {
        var query = AuditingCursorQueryFactory.Create(ProviderName, strategy);
        var parameters = CreateCursorParameters(boundary, offset, pageSize);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            new CommandDefinition(
                "SET STATISTICS XML ON",
                cancellationToken: cancellationToken));
        try
        {
            var plans = new Dictionary<string, string>(StringComparer.Ordinal);
            if (query.CountSql is not null)
            {
                plans["count"] = await CaptureCursorPlanAsync(
                    connection,
                    query.CountSql,
                    parameters,
                    cancellationToken);
            }

            plans["list"] = await CaptureCursorPlanAsync(
                connection,
                query.ListSql,
                parameters,
                cancellationToken);
            return plans;
        }
        finally
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "SET STATISTICS XML OFF",
                    cancellationToken: cancellationToken));
        }
    }

    public override async Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<string>(
                new CommandDefinition(
                    "SELECT @@VERSION",
                    cancellationToken: cancellationToken))
            ?? "unknown";
    }

    public override ValueTask DisposeAsync() => container.DisposeAsync();

    private static async Task<string> CapturePlanAsync(
        SqlConnection connection,
        string statement,
        AuditingQueryScenario scenario,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = statement;
        command.CommandTimeout = 120;
        AddParameters(command, scenario);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await ReadStatisticsXmlAsync(reader, cancellationToken);
    }

    private static async Task<string> CaptureCursorPlanAsync(
        SqlConnection connection,
        string statement,
        object parameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = statement;
        command.CommandTimeout = 120;
        var values = (dynamic)parameters;
        command.Parameters.Add("@Offset", SqlDbType.Int).Value = values.Offset;
        command.Parameters.Add("@PageSize", SqlDbType.Int).Value = values.PageSize;
        command.Parameters.Add("@FetchSize", SqlDbType.Int).Value = values.FetchSize;
        command.Parameters.Add(
            "@CursorOccurredAtUtc",
            SqlDbType.DateTimeOffset).Value = values.CursorOccurredAtUtc;
        command.Parameters.Add("@CursorId", SqlDbType.UniqueIdentifier).Value =
            values.CursorId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await ReadStatisticsXmlAsync(reader, cancellationToken);
    }

    private static object CreateCursorParameters(
        AuditingCursorBoundary boundary,
        int offset,
        int pageSize) =>
        new
        {
            FromUtc = (DateTimeOffset?)null,
            ToUtc = (DateTimeOffset?)null,
            HttpMethod = (string?)null,
            StatusCode = (int?)null,
            PathContains = (string?)null,
            Offset = offset,
            PageSize = pageSize,
            FetchSize = pageSize + 1,
            CursorOccurredAtUtc = boundary.OccurredAtUtc,
            CursorId = boundary.Id,
        };

    private sealed class SqlServerCursorBoundaryRecord
    {
        public Guid Id { get; init; }

        public DateTimeOffset OccurredAtUtc { get; init; }
    }

    private static void AddParameters(
        SqlCommand command,
        AuditingQueryScenario scenario)
    {
        command.Parameters.Add("@FromUtc", SqlDbType.DateTimeOffset).Value =
            scenario.FromUtc is { } fromUtc ? fromUtc : DBNull.Value;
        command.Parameters.Add("@ToUtc", SqlDbType.DateTimeOffset).Value =
            scenario.ToUtc is { } toUtc ? toUtc : DBNull.Value;
        command.Parameters.Add("@HttpMethod", SqlDbType.VarChar, 16).Value =
            scenario.HttpMethod is { } method ? method : DBNull.Value;
        command.Parameters.Add("@StatusCode", SqlDbType.Int).Value =
            scenario.StatusCode is { } statusCode ? statusCode : DBNull.Value;
        command.Parameters.Add("@PathContains", SqlDbType.NVarChar, 512).Value =
            scenario.PathContains is { } path ? path : DBNull.Value;
        command.Parameters.Add("@Offset", SqlDbType.Int).Value = scenario.Offset;
        command.Parameters.Add("@PageSize", SqlDbType.Int).Value = scenario.PageSize;
    }

    private static async Task<string> ReadStatisticsXmlAsync(
        DbDataReader reader,
        CancellationToken cancellationToken)
    {
        var plans = new StringBuilder();
        do
        {
            var isPlanResult = reader.FieldCount == 1
                && reader.GetName(0).Contains("Showplan", StringComparison.OrdinalIgnoreCase);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (isPlanResult
                    && Convert.ToString(
                        reader.GetValue(0),
                        CultureInfo.InvariantCulture) is { } plan
                    && plan.Contains("<ShowPlanXML", StringComparison.Ordinal))
                {
                    plans.AppendLine(plan);
                }
            }
        }
        while (await reader.NextResultAsync(cancellationToken));

        return plans.ToString();
    }

}

internal sealed class MySqlAuditingBenchmarkDatabase(
    MySqlContainer container,
    string connectionString)
    : AuditingBenchmarkDatabase(connectionString), IAuditingCursorBenchmarkDatabase
{
    public const string Image = "mysql:8.0";

    public override string ProviderName => "mysql";

    public override string ContainerImage => Image;

    public override string PlanFileExtension => "explain.json";

    public static async Task<MySqlAuditingBenchmarkDatabase> StartContainerAsync(
        string password,
        CancellationToken cancellationToken)
    {
        var container = new MySqlBuilder(Image)
            .WithCommand(
                "--local-infile=1",
                "--log-bin-trust-function-creators=1")
            .WithDatabase("fullnet_benchmark")
            .WithUsername("fullnet")
            .WithPassword(password)
            .Build();
        await container.StartAsync(cancellationToken);
        try
        {
            var connectionString = MySqlConnectionStringPolicy.Create(
                container.GetConnectionString(),
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false);
            return new MySqlAuditingBenchmarkDatabase(container, connectionString);
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }

    public override async Task SeedAsync(
        int rows,
        DateTimeOffset referenceUtc,
        CancellationToken cancellationToken)
    {
        var builder = new MySqlConnectionStringBuilder(ConnectionString)
        {
            AllowLoadLocalInfile = true,
        };
        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        foreach (var (start, count) in SeedBatches(rows))
        {
            using var table = CreateSeedTable(
                start,
                count,
                rows,
                referenceUtc,
                binaryGuids: true,
                dateTimeOffset: false);
            var bulkCopy = new MySqlBulkCopy(connection)
            {
                DestinationTableName = "fn_auditing_access_log",
                BulkCopyTimeout = 120,
            };
            for (var index = 0; index < table.Columns.Count; index++)
            {
                var column = table.Columns[index];
                if (column.ColumnName == "Id")
                {
                    bulkCopy.ColumnMappings.Add(
                        new MySqlBulkCopyColumnMapping(
                            index,
                            "@benchmark_id",
                            "`Id` = UNHEX(@benchmark_id)"));
                }
                else
                {
                    bulkCopy.ColumnMappings.Add(
                        new MySqlBulkCopyColumnMapping(index, column.ColumnName));
                }
            }

            var result = await bulkCopy.WriteToServerAsync(table, cancellationToken);
            if (result.RowsInserted != count || result.Warnings.Count != 0)
            {
                throw new InvalidOperationException(
                    $"MySQL 批量准备数据不完整：期望 {count}，实际 {result.RowsInserted}，"
                    + $"警告 {result.Warnings.Count}。");
            }
        }
    }

    public override async Task<AuditingQueryPageResult> ExecutePageAsync(
        AuditingQueryScenario scenario,
        CancellationToken cancellationToken) =>
        await ExecuteMySqlPageAsync(
            AuditingMySqlQueryStrategy.CurrentOptimizer,
            scenario,
            cancellationToken);

    public async Task<AuditingQueryPageResult> ExecuteMySqlPageAsync(
        AuditingMySqlQueryStrategy strategy,
        AuditingQueryScenario scenario,
        CancellationToken cancellationToken)
    {
        var query = AuditingMySqlQueryFactory.Create(strategy);
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(
                query.PageSql,
                CreateParameters(scenario),
                commandTimeout: 120,
                cancellationToken: cancellationToken));
        var total = await grid.ReadSingleAsync<long>();
        var rows = (await grid.ReadAsync()).ToList();
        return CreatePageResult(total, rows);
    }

    public override async Task<IReadOnlyDictionary<string, string>> CapturePlansAsync(
        AuditingQueryScenario scenario,
        CancellationToken cancellationToken) =>
        await CaptureMySqlPlansAsync(
            AuditingMySqlQueryStrategy.CurrentOptimizer,
            scenario,
            cancellationToken);

    public async Task<IReadOnlyDictionary<string, string>> CaptureMySqlPlansAsync(
        AuditingMySqlQueryStrategy strategy,
        AuditingQueryScenario scenario,
        CancellationToken cancellationToken)
    {
        var query = AuditingMySqlQueryFactory.Create(strategy);
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["count"] = await CapturePlanAsync(
                connection,
                query.CountSql,
                scenario,
                cancellationToken),
            ["list"] = await CapturePlanAsync(
                connection,
                query.ListSql,
                scenario,
                cancellationToken),
        };
    }

    public override async Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<string>(
                new CommandDefinition(
                    "SELECT VERSION()",
                    cancellationToken: cancellationToken))
            ?? "unknown";
    }

    public async Task<AuditingCursorBoundary> FindDeepCursorBoundaryAsync(
        int offset,
        CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        var record = await connection.QuerySingleAsync<MySqlCursorBoundaryRecord>(
            new CommandDefinition(
                """
                SELECT Id, OccurredAtUtc
                FROM fn_auditing_access_log
                ORDER BY OccurredAtUtc DESC, Id DESC
                LIMIT 1 OFFSET @BoundaryOffset
                """,
                new { BoundaryOffset = offset - 1 },
                commandTimeout: 120,
                cancellationToken: cancellationToken));
        return new AuditingCursorBoundary(
            new DateTimeOffset(record.OccurredAtUtc, TimeSpan.Zero),
            record.Id);
    }

    public async Task<AuditingQueryPageResult> ExecuteCursorComparisonAsync(
        AuditingCursorQueryStrategy strategy,
        AuditingCursorBoundary boundary,
        int offset,
        int pageSize,
        int totalRows,
        CancellationToken cancellationToken)
    {
        var query = AuditingCursorQueryFactory.Create(ProviderName, strategy);
        var parameters = CreateCursorParameters(boundary, offset, pageSize);
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        if (query.CountSql is not null)
        {
            await using var grid = await connection.QueryMultipleAsync(
                new CommandDefinition(
                    $"{query.CountSql.TrimEnd()};{Environment.NewLine}{query.ListSql}",
                    parameters,
                    commandTimeout: 120,
                    cancellationToken: cancellationToken));
            var count = await grid.ReadSingleAsync<long>();
            var offsetRows = (await grid.ReadAsync()).ToList();
            return CreatePageResult(count, offsetRows);
        }

        var cursorRows = (await connection.QueryAsync(
            new CommandDefinition(
                query.ListSql,
                parameters,
                commandTimeout: 120,
                cancellationToken: cancellationToken))).ToList();
        return CreatePageResult(totalRows, cursorRows);
    }

    public async Task<IReadOnlyDictionary<string, string>>
        CaptureCursorComparisonPlansAsync(
            AuditingCursorQueryStrategy strategy,
            AuditingCursorBoundary boundary,
            int offset,
            int pageSize,
            CancellationToken cancellationToken)
    {
        var query = AuditingCursorQueryFactory.Create(ProviderName, strategy);
        var parameters = CreateCursorParameters(boundary, offset, pageSize);
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var plans = new Dictionary<string, string>(StringComparer.Ordinal);
        if (query.CountSql is not null)
        {
            plans["count"] = await CaptureCursorPlanAsync(
                connection,
                query.CountSql,
                parameters,
                cancellationToken);
        }

        plans["list"] = await CaptureCursorPlanAsync(
            connection,
            query.ListSql,
            parameters,
            cancellationToken);
        return plans;
    }

    public override ValueTask DisposeAsync() => container.DisposeAsync();

    private static async Task<string> CapturePlanAsync(
        MySqlConnection connection,
        string statement,
        AuditingQueryScenario scenario,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"EXPLAIN FORMAT=JSON {statement}";
        command.CommandTimeout = 120;
        AddParameters(command, scenario);
        return Convert.ToString(
                   await command.ExecuteScalarAsync(cancellationToken),
                   CultureInfo.InvariantCulture)
               ?? string.Empty;
    }

    private static async Task<string> CaptureCursorPlanAsync(
        MySqlConnection connection,
        string statement,
        object parameters,
        CancellationToken cancellationToken) =>
        Convert.ToString(
            await connection.ExecuteScalarAsync(
                new CommandDefinition(
                    $"EXPLAIN FORMAT=JSON {statement}",
                    parameters,
                    commandTimeout: 120,
                    cancellationToken: cancellationToken)),
            CultureInfo.InvariantCulture) ?? string.Empty;

    private static object CreateCursorParameters(
        AuditingCursorBoundary boundary,
        int offset,
        int pageSize) =>
        new
        {
            FromUtc = (DateTime?)null,
            ToUtc = (DateTime?)null,
            HttpMethod = (string?)null,
            StatusCode = (int?)null,
            PathContains = (string?)null,
            Offset = offset,
            PageSize = pageSize,
            FetchSize = pageSize + 1,
            CursorOccurredAtUtc = boundary.OccurredAtUtc.UtcDateTime,
            CursorId = boundary.Id,
        };

    private sealed class MySqlCursorBoundaryRecord
    {
        public Guid Id { get; init; }

        public DateTime OccurredAtUtc { get; init; }
    }

    private static void AddParameters(
        MySqlCommand command,
        AuditingQueryScenario scenario)
    {
        command.Parameters.Add("@FromUtc", MySqlDbType.DateTime).Value =
            scenario.FromUtc is { } fromUtc ? fromUtc.UtcDateTime : DBNull.Value;
        command.Parameters.Add("@ToUtc", MySqlDbType.DateTime).Value =
            scenario.ToUtc is { } toUtc ? toUtc.UtcDateTime : DBNull.Value;
        command.Parameters.Add("@HttpMethod", MySqlDbType.VarChar, 16).Value =
            scenario.HttpMethod is { } method ? method : DBNull.Value;
        command.Parameters.Add("@StatusCode", MySqlDbType.Int32).Value =
            scenario.StatusCode is { } statusCode ? statusCode : DBNull.Value;
        command.Parameters.Add("@PathContains", MySqlDbType.VarChar, 512).Value =
            scenario.PathContains is { } path ? path : DBNull.Value;
        command.Parameters.Add("@Offset", MySqlDbType.Int32).Value = scenario.Offset;
        command.Parameters.Add("@PageSize", MySqlDbType.Int32).Value = scenario.PageSize;
    }
}
