using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Testcontainers.MsSql;
using Testcontainers.MySql;

namespace Full.NET.Benchmarks.Jobs;

public sealed record JobsBacklogPlanArtifact(
    string FileName,
    string Content);

internal abstract class JobsBacklogBenchmarkDatabase : IAsyncDisposable
{
    private const string Password = "FullNet_Benchmark!123";
    private const int SeedBatchSize = 10_000;

    protected JobsBacklogBenchmarkDatabase(string connectionString)
    {
        ConnectionString = connectionString;
    }

    public abstract string ProviderName { get; }

    public abstract string ContainerImage { get; }

    protected string ConnectionString { get; }

    public static async Task<JobsBacklogBenchmarkDatabase> StartAsync(
        string provider,
        CancellationToken cancellationToken)
    {
        JobsBacklogBenchmarkDatabase database = provider switch
        {
            "sqlserver" =>
                await SqlServerJobsBacklogBenchmarkDatabase.StartContainerAsync(
                    Password,
                    cancellationToken),
            "mysql" =>
                await MySqlJobsBacklogBenchmarkDatabase.StartContainerAsync(
                    Password,
                    cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "不支持的 Jobs backlog 基准数据库 Provider。"),
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
        JobsBacklogQueryBenchmarkOptions options,
        CancellationToken cancellationToken);

    public abstract Task<JobsBacklogQueryResult> ExecuteAsync(
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken);

    public abstract Task<IReadOnlyList<JobsBacklogPlanArtifact>>
        CapturePlansAsync(
            DateTimeOffset observedAtUtc,
            CancellationToken cancellationToken);

    public abstract Task<TimeSpan> SetIndexVariantAsync(
        JobsBacklogIndexVariant variant,
        CancellationToken cancellationToken);

    public abstract Task<long> GetCandidateIndexSizeBytesAsync(
        CancellationToken cancellationToken);

    public abstract Task<TimeSpan> MeasureMutationAsync(
        JobsBacklogMutationKind mutation,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken);

    public abstract Task<string> GetVersionAsync(
        CancellationToken cancellationToken);

    public abstract ValueTask DisposeAsync();

    protected static DataTable CreateSqlServerSeedTable(
        int startIndex,
        int count,
        JobsBacklogQueryBenchmarkOptions options)
    {
        var table = CreateSeedTableSchema(
            idType: typeof(Guid),
            timestampType: typeof(DateTimeOffset));
        for (var offset = 0; offset < count; offset++)
        {
            var row = JobsBacklogDataset.CreateRow(
                startIndex + offset,
                options.Rows,
                options.ReferenceUtc);
            var dataRow = table.NewRow();
            dataRow["Id"] = row.Id;
            dataRow["TenantId"] = row.TenantId is { } tenantId
                ? tenantId
                : DBNull.Value;
            dataRow["JobDefinitionId"] = row.JobDefinitionId;
            dataRow["Status"] = row.Status;
            dataRow["TriggerKind"] = row.TriggerKind;
            dataRow["NextAttemptAtUtc"] =
                row.NextAttemptAtUtc is { } nextAttemptAtUtc
                    ? nextAttemptAtUtc
                    : DBNull.Value;
            dataRow["AttemptCount"] = row.AttemptCount;
            dataRow["CreatedAtUtc"] = row.CreatedAtUtc;
            table.Rows.Add(dataRow);
        }

        return table;
    }

    protected static DataTable CreateMySqlSeedTable(
        int startIndex,
        int count,
        JobsBacklogQueryBenchmarkOptions options)
    {
        var table = CreateSeedTableSchema(
            idType: typeof(string),
            timestampType: typeof(DateTime));
        for (var offset = 0; offset < count; offset++)
        {
            var row = JobsBacklogDataset.CreateRow(
                startIndex + offset,
                options.Rows,
                options.ReferenceUtc);
            var dataRow = table.NewRow();
            dataRow["Id"] = ToMySqlGuid(row.Id);
            dataRow["TenantId"] = row.TenantId is { } tenantId
                ? ToMySqlGuid(tenantId)
                : DBNull.Value;
            dataRow["JobDefinitionId"] = ToMySqlGuid(row.JobDefinitionId);
            dataRow["Status"] = row.Status;
            dataRow["TriggerKind"] = row.TriggerKind;
            dataRow["NextAttemptAtUtc"] =
                row.NextAttemptAtUtc is { } nextAttemptAtUtc
                    ? nextAttemptAtUtc.UtcDateTime
                    : DBNull.Value;
            dataRow["AttemptCount"] = row.AttemptCount;
            dataRow["CreatedAtUtc"] = row.CreatedAtUtc.UtcDateTime;
            table.Rows.Add(dataRow);
        }

        return table;
    }

    protected static IEnumerable<(int Start, int Count)> SeedBatches(int rows)
    {
        for (var start = 0; start < rows; start += SeedBatchSize)
        {
            yield return (
                start,
                Math.Min(SeedBatchSize, rows - start));
        }
    }

    protected static object CreateMutationInsertParameters(
        int index,
        DateTimeOffset observedAtUtc) =>
        new
        {
            Id = Guid.CreateVersion7(
                observedAtUtc.AddMilliseconds(index + 1).UtcDateTime),
            JobDefinitionId = JobsBacklogDataset
                .CreateRow(0, JobsBacklogDataset.BucketCount, observedAtUtc)
                .JobDefinitionId,
            Status = JobExecutionStatuses.Pending,
            TriggerKind = JobTriggerKinds.Manual,
            CreatedAtUtc = observedAtUtc.AddMilliseconds(index + 1),
        };

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var provider = ProviderName == "sqlserver"
            ? DatabaseProvider.SqlServer
            : DatabaseProvider.MySql;
        var result = await new DbUpMigrationRunner(
                Options.Create(new DatabaseOptions
                {
                    Provider = provider,
                    ConnectionString = ConnectionString,
                    MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
                    CommandTimeoutSeconds = 120,
                }),
                NullLoggerFactory.Instance,
                Options.Create(new UuidBinaryContractOptions
                {
                    MaintenanceMode = true,
                    BackupVerified = true,
                    LegacyWritersStopped = true,
                    DestructiveDdlApprovalId =
                        "benchmark-jobs-backlog-uuid",
                }),
                Options.Create(new PreV1NamingContractOptions
                {
                    MaintenanceMode = true,
                    BackupVerified = true,
                    LegacyWritersStopped = true,
                    LegacyOutboxDrained = true,
                    DestructiveDdlApprovalId =
                        "benchmark-jobs-backlog-naming",
                }))
            .MigrateAsync(cancellationToken);
        if (!result.Successful)
        {
            throw new InvalidOperationException(
                "Jobs backlog 基准数据库迁移失败。");
        }
    }

    private static DataTable CreateSeedTableSchema(
        Type idType,
        Type timestampType)
    {
        var table = new DataTable
        {
            Locale = CultureInfo.InvariantCulture,
        };
        table.Columns.Add("Id", idType);
        table.Columns.Add("TenantId", idType);
        table.Columns.Add("JobDefinitionId", idType);
        table.Columns.Add("Status", typeof(string));
        table.Columns.Add("TriggerKind", typeof(string));
        table.Columns.Add("NextAttemptAtUtc", timestampType);
        table.Columns.Add("AttemptCount", typeof(int));
        table.Columns.Add("CreatedAtUtc", timestampType);
        return table;
    }

    private static string ToMySqlGuid(Guid value) =>
        Convert.ToHexString(value.ToByteArray(bigEndian: true));
}

internal sealed class SqlServerJobsBacklogBenchmarkDatabase(
    MsSqlContainer container,
    string connectionString)
    : JobsBacklogBenchmarkDatabase(connectionString)
{
    public const string Image =
        "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04";

    public override string ProviderName => "sqlserver";

    public override string ContainerImage => Image;

    public static async Task<SqlServerJobsBacklogBenchmarkDatabase>
        StartContainerAsync(
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
            await using (var connection =
                new SqlConnection(baseConnectionString))
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        "CREATE DATABASE [fullnet_benchmark];",
                        cancellationToken: cancellationToken));
            }

            var connectionString =
                new SqlConnectionStringBuilder(baseConnectionString)
                {
                    InitialCatalog = "fullnet_benchmark",
                }.ConnectionString;
            return new SqlServerJobsBacklogBenchmarkDatabase(
                container,
                connectionString);
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }

    public override async Task SeedAsync(
        JobsBacklogQueryBenchmarkOptions options,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        foreach (var (start, count) in SeedBatches(options.Rows))
        {
            using var table = CreateSqlServerSeedTable(
                start,
                count,
                options);
            using var bulkCopy = new SqlBulkCopy(
                connection,
                SqlBulkCopyOptions.TableLock,
                externalTransaction: null)
            {
                DestinationTableName = "dbo.fn_jobs_execution",
                BatchSize = count,
                BulkCopyTimeout = 120,
            };
            foreach (DataColumn column in table.Columns)
            {
                bulkCopy.ColumnMappings.Add(
                    column.ColumnName,
                    column.ColumnName);
            }

            await bulkCopy.WriteToServerAsync(table, cancellationToken);
        }
    }

    public override async Task<JobsBacklogQueryResult> ExecuteAsync(
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.QuerySingleAsync<JobsBacklogQueryResult>(
            new CommandDefinition(
                JobsBacklogQuerySql.ForProvider(ProviderName),
                new
                {
                    ObservedAtUtc = observedAtUtc,
                    PendingStatus = JobExecutionStatuses.Pending,
                },
                commandTimeout: 120,
                cancellationToken: cancellationToken));
    }

    public override async Task<IReadOnlyList<JobsBacklogPlanArtifact>>
        CapturePlansAsync(
            DateTimeOffset observedAtUtc,
            CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            new CommandDefinition(
                "SET STATISTICS XML ON",
                cancellationToken: cancellationToken));
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                JobsBacklogQuerySql.ForProvider(ProviderName);
            command.CommandTimeout = 120;
            command.Parameters.Add(
                "@ObservedAtUtc",
                SqlDbType.DateTimeOffset).Value = observedAtUtc;
            command.Parameters.Add(
                "@PendingStatus",
                SqlDbType.VarChar,
                32).Value = JobExecutionStatuses.Pending;
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            var plan = await ReadStatisticsXmlAsync(
                reader,
                cancellationToken);
            return
            [
                new JobsBacklogPlanArtifact(
                    "actual.showplan.xml",
                    plan),
            ];
        }
        finally
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "SET STATISTICS XML OFF",
                    cancellationToken: cancellationToken));
        }
    }

    public override async Task<TimeSpan> SetIndexVariantAsync(
        JobsBacklogIndexVariant variant,
        CancellationToken cancellationToken)
    {
        var definition = JobsBacklogIndexCandidate.ForProvider(
            ProviderName);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var exists = await CandidateIndexExistsAsync(
            connection,
            cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        if (variant == JobsBacklogIndexVariant.Candidate && !exists)
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    definition.CreateSql,
                    commandTimeout: 120,
                    cancellationToken: cancellationToken));
        }
        else if (variant == JobsBacklogIndexVariant.Baseline && exists)
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    definition.DropSql,
                    commandTimeout: 120,
                    cancellationToken: cancellationToken));
        }

        stopwatch.Stop();
        var actualExists = await CandidateIndexExistsAsync(
            connection,
            cancellationToken);
        if (actualExists
            != (variant == JobsBacklogIndexVariant.Candidate))
        {
            throw new InvalidOperationException(
                $"SQL Server 候选索引状态未切换为 {variant}。");
        }

        return stopwatch.Elapsed;
    }

    public override async Task<long> GetCandidateIndexSizeBytesAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                JobsBacklogIndexSizeSql.ForProvider(ProviderName),
                new
                {
                    IndexName = JobsBacklogIndexCandidate.IndexName,
                },
                cancellationToken: cancellationToken));
    }

    public override async Task<TimeSpan> MeasureMutationAsync(
        JobsBacklogMutationKind mutation,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken) =>
        mutation switch
        {
            JobsBacklogMutationKind.TriggerInsert =>
                await MeasureInsertAsync(
                    observedAtUtc,
                    cancellationToken),
            JobsBacklogMutationKind.Claim =>
                await MeasureClaimAsync(
                    observedAtUtc,
                    cancellationToken),
            JobsBacklogMutationKind.TerminalSuccess =>
                await MeasureTerminalSuccessAsync(
                    observedAtUtc,
                    cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(mutation),
                mutation,
                "不支持的 Jobs backlog 写路径探针。"),
        };

    public override async Task<string> GetVersionAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<string>(
                new CommandDefinition(
                    "SELECT @@VERSION",
                    cancellationToken: cancellationToken))
            ?? "unknown";
    }

    public override ValueTask DisposeAsync() => container.DisposeAsync();

    private static async Task<bool> CandidateIndexExistsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                """
                SELECT COUNT(*)
                FROM sys.indexes
                WHERE object_id =
                          OBJECT_ID(N'dbo.fn_jobs_execution')
                  AND name = @IndexName
                """,
                new
                {
                    IndexName = JobsBacklogIndexCandidate.IndexName,
                },
                cancellationToken: cancellationToken)) == 1;

    private async Task<TimeSpan> MeasureInsertAsync(
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var affected = 0;
            for (var index = 0; index < MutationBatchSize; index++)
            {
                affected += await connection.ExecuteAsync(
                    new CommandDefinition(
                        JobsBacklogMutationSql
                            .ForProvider(ProviderName)
                            .TriggerInsertSql,
                        CreateMutationInsertParameters(
                            index,
                            observedAtUtc),
                        transaction,
                        cancellationToken: cancellationToken));
            }

            stopwatch.Stop();
            if (affected != MutationBatchSize)
            {
                throw new InvalidOperationException(
                    $"SQL Server trigger_insert 期望写入 "
                    + $"{MutationBatchSize} 行，实际 {affected} 行。");
            }

            return stopwatch.Elapsed;
        }
        finally
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private async Task<TimeSpan> MeasureClaimAsync(
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var claimed = (await ClaimAsync(
                    connection,
                    transaction,
                    observedAtUtc,
                    cancellationToken))
                .Count;
            stopwatch.Stop();
            if (claimed != MutationBatchSize)
            {
                throw new InvalidOperationException(
                    $"SQL Server claim 期望领取 {MutationBatchSize} 行，"
                    + $"实际 {claimed} 行。");
            }

            return stopwatch.Elapsed;
        }
        finally
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private async Task<TimeSpan> MeasureTerminalSuccessAsync(
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var leaseId = Guid.CreateVersion7();
            var claimed = await ClaimAsync(
                connection,
                transaction,
                observedAtUtc,
                cancellationToken,
                leaseId);
            var stopwatch = Stopwatch.StartNew();
            var affected = 0;
            foreach (var id in claimed)
            {
                affected += await connection.ExecuteAsync(
                    new CommandDefinition(
                        JobsBacklogMutationSql
                            .ForProvider(ProviderName)
                            .TerminalSuccessSql,
                        new
                        {
                            Id = id,
                            LeaseId = leaseId,
                            RunningStatus =
                                JobExecutionStatuses.Running,
                            SucceededStatus =
                                JobExecutionStatuses.Succeeded,
                            FinishedAtUtc = observedAtUtc,
                        },
                        transaction,
                        cancellationToken: cancellationToken));
            }

            stopwatch.Stop();
            if (affected != MutationBatchSize)
            {
                throw new InvalidOperationException(
                    $"SQL Server terminal_success 期望更新 "
                    + $"{MutationBatchSize} 行，实际 {affected} 行。");
            }

            return stopwatch.Elapsed;
        }
        finally
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private async Task<IReadOnlyList<Guid>> ClaimAsync(
        SqlConnection connection,
        DbTransaction transaction,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken,
        Guid? fixedLeaseId = null)
    {
        var leaseId = fixedLeaseId ?? Guid.CreateVersion7();
        var records = await connection.QueryAsync<JobExecutionRecord>(
            new CommandDefinition(
                JobsBacklogMutationSql
                    .ForProvider(ProviderName)
                    .ClaimSelectSql,
                new
                {
                    BatchSize = MutationBatchSize,
                    PendingStatus = JobExecutionStatuses.Pending,
                    RunningStatus = JobExecutionStatuses.Running,
                    Now = observedAtUtc,
                    LeaseId = leaseId,
                    LeaseExpiresAtUtc = observedAtUtc.AddMinutes(5),
                },
                transaction,
                commandTimeout: 120,
                cancellationToken: cancellationToken));
        return records.Select(record => record.Id).ToArray();
    }

    private static async Task<string> ReadStatisticsXmlAsync(
        DbDataReader reader,
        CancellationToken cancellationToken)
    {
        var plans = new StringBuilder();
        do
        {
            var isPlanResult = reader.FieldCount == 1
                && reader.GetName(0).Contains(
                    "Showplan",
                    StringComparison.OrdinalIgnoreCase);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (isPlanResult
                    && Convert.ToString(
                        reader.GetValue(0),
                        CultureInfo.InvariantCulture) is { } plan
                    && plan.Contains(
                        "<ShowPlanXML",
                        StringComparison.Ordinal))
                {
                    plans.AppendLine(plan);
                }
            }
        }
        while (await reader.NextResultAsync(cancellationToken));

        return plans.ToString();
    }

    private const int MutationBatchSize = 20;
}

internal sealed class MySqlJobsBacklogBenchmarkDatabase(
    MySqlContainer container,
    string connectionString)
    : JobsBacklogBenchmarkDatabase(connectionString)
{
    public const string Image = "mysql:8.0";

    private long? _baselineTotalIndexSizeBytes;

    public override string ProviderName => "mysql";

    public override string ContainerImage => Image;

    public static async Task<MySqlJobsBacklogBenchmarkDatabase>
        StartContainerAsync(
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
            return new MySqlJobsBacklogBenchmarkDatabase(
                container,
                connectionString);
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }

    public override async Task SeedAsync(
        JobsBacklogQueryBenchmarkOptions options,
        CancellationToken cancellationToken)
    {
        var connectionString =
            new MySqlConnectionStringBuilder(ConnectionString)
            {
                AllowLoadLocalInfile = true,
            }.ConnectionString;
        await using var connection =
            new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        foreach (var (start, count) in SeedBatches(options.Rows))
        {
            using var table = CreateMySqlSeedTable(
                start,
                count,
                options);
            var bulkCopy = new MySqlBulkCopy(connection)
            {
                DestinationTableName = "fn_jobs_execution",
                BulkCopyTimeout = 120,
            };
            for (var index = 0; index < table.Columns.Count; index++)
            {
                var columnName = table.Columns[index].ColumnName;
                if (columnName is "Id" or "TenantId"
                    or "JobDefinitionId")
                {
                    var variable =
                        $"@benchmark_{columnName.ToLowerInvariant()}";
                    bulkCopy.ColumnMappings.Add(
                        new MySqlBulkCopyColumnMapping(
                            index,
                            variable,
                            $"`{columnName}` = UNHEX({variable})"));
                }
                else
                {
                    bulkCopy.ColumnMappings.Add(
                        new MySqlBulkCopyColumnMapping(
                            index,
                            columnName));
                }
            }

            var result = await bulkCopy.WriteToServerAsync(
                table,
                cancellationToken);
            if (result.RowsInserted != count
                || result.Warnings.Count != 0)
            {
                throw new InvalidOperationException(
                    $"MySQL Jobs backlog 批量数据不完整：期望 {count}，"
                    + $"实际 {result.RowsInserted}，警告 {result.Warnings.Count}。");
            }
        }
    }

    public override async Task<JobsBacklogQueryResult> ExecuteAsync(
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new MySqlConnection(ConnectionString);
        var row = await connection.QuerySingleAsync<MySqlBacklogRow>(
            new CommandDefinition(
                JobsBacklogQuerySql.ForProvider(ProviderName),
                new
                {
                    ObservedAtUtc = observedAtUtc.UtcDateTime,
                    PendingStatus = JobExecutionStatuses.Pending,
                },
                commandTimeout: 120,
                cancellationToken: cancellationToken));
        return new JobsBacklogQueryResult(
            row.PendingCount,
            AsUtc(row.OldestClaimableCreatedAtUtc),
            row.DueRetryCount,
            AsUtc(row.OldestDueRetryAtUtc));
    }

    public override async Task<IReadOnlyList<JobsBacklogPlanArtifact>>
        CapturePlansAsync(
            DateTimeOffset observedAtUtc,
            CancellationToken cancellationToken)
    {
        await using var connection =
            new MySqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var parameters = new
        {
            ObservedAtUtc = observedAtUtc.UtcDateTime,
            PendingStatus = JobExecutionStatuses.Pending,
        };
        var statement = JobsBacklogQuerySql.ForProvider(ProviderName);
        var json = Convert.ToString(
            await connection.ExecuteScalarAsync(
                new CommandDefinition(
                    $"EXPLAIN FORMAT=JSON {statement}",
                    parameters,
                    commandTimeout: 120,
                    cancellationToken: cancellationToken)),
            CultureInfo.InvariantCulture) ?? string.Empty;
        var analyze = Convert.ToString(
            await connection.ExecuteScalarAsync(
                new CommandDefinition(
                    $"EXPLAIN ANALYZE {statement}",
                    parameters,
                    commandTimeout: 120,
                    cancellationToken: cancellationToken)),
            CultureInfo.InvariantCulture) ?? string.Empty;
        return
        [
            new JobsBacklogPlanArtifact(
                "estimated.explain.json",
                json),
            new JobsBacklogPlanArtifact(
                "actual.explain-analyze.txt",
                analyze),
        ];
    }

    public override async Task<TimeSpan> SetIndexVariantAsync(
        JobsBacklogIndexVariant variant,
        CancellationToken cancellationToken)
    {
        var definition = JobsBacklogIndexCandidate.ForProvider(
            ProviderName);
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var exists = await CandidateIndexExistsAsync(
            connection,
            cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        if (variant == JobsBacklogIndexVariant.Candidate && !exists)
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    definition.CreateSql,
                    commandTimeout: 120,
                    cancellationToken: cancellationToken));
        }
        else if (variant == JobsBacklogIndexVariant.Baseline && exists)
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    definition.DropSql,
                    commandTimeout: 120,
                    cancellationToken: cancellationToken));
        }

        stopwatch.Stop();
        var actualExists = await CandidateIndexExistsAsync(
            connection,
            cancellationToken);
        if (actualExists
            != (variant == JobsBacklogIndexVariant.Candidate))
        {
            throw new InvalidOperationException(
                $"MySQL 候选索引状态未切换为 {variant}。");
        }

        if (variant == JobsBacklogIndexVariant.Baseline
            && _baselineTotalIndexSizeBytes is null)
        {
            _baselineTotalIndexSizeBytes =
                await GetTotalIndexSizeBytesAsync(
                    connection,
                    cancellationToken);
        }

        return stopwatch.Elapsed;
    }

    public override async Task<long> GetCandidateIndexSizeBytesAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        var baseline = _baselineTotalIndexSizeBytes
            ?? throw new InvalidOperationException(
                "MySQL 候选索引体积要求先记录 baseline 总索引体积。");
        var candidateTotal = await GetTotalIndexSizeBytesAsync(
            connection,
            cancellationToken);
        return Math.Max(0, candidateTotal - baseline);
    }

    public override async Task<TimeSpan> MeasureMutationAsync(
        JobsBacklogMutationKind mutation,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken) =>
        mutation switch
        {
            JobsBacklogMutationKind.TriggerInsert =>
                await MeasureInsertAsync(
                    observedAtUtc,
                    cancellationToken),
            JobsBacklogMutationKind.Claim =>
                await MeasureClaimAsync(
                    observedAtUtc,
                    cancellationToken),
            JobsBacklogMutationKind.TerminalSuccess =>
                await MeasureTerminalSuccessAsync(
                    observedAtUtc,
                    cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(mutation),
                mutation,
                "不支持的 Jobs backlog 写路径探针。"),
        };

    public override async Task<string> GetVersionAsync(
        CancellationToken cancellationToken)
    {
        await using var connection =
            new MySqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<string>(
                new CommandDefinition(
                    "SELECT VERSION()",
                    cancellationToken: cancellationToken))
            ?? "unknown";
    }

    public override ValueTask DisposeAsync() => container.DisposeAsync();

    private static async Task<bool> CandidateIndexExistsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'fn_jobs_execution'
                  AND INDEX_NAME = @IndexName
                """,
                new
                {
                    IndexName = JobsBacklogIndexCandidate.IndexName,
                },
                cancellationToken: cancellationToken)) > 0;

    private static async Task<long> GetTotalIndexSizeBytesAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await connection.ExecuteAsync(
            new CommandDefinition(
                JobsBacklogIndexSizeSql.MySqlStatisticsRefreshSql,
                cancellationToken: cancellationToken));
        await connection.ExecuteAsync(
            new CommandDefinition(
                JobsBacklogIndexSizeSql.MySqlAnalyzeTableSql,
                commandTimeout: 120,
                cancellationToken: cancellationToken));
        return await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                JobsBacklogIndexSizeSql.ForProvider("mysql"),
                cancellationToken: cancellationToken));
    }

    private async Task<TimeSpan> MeasureInsertAsync(
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var affected = 0;
            for (var index = 0; index < MutationBatchSize; index++)
            {
                affected += await connection.ExecuteAsync(
                    new CommandDefinition(
                        JobsBacklogMutationSql
                            .ForProvider(ProviderName)
                            .TriggerInsertSql,
                        CreateMutationInsertParameters(
                            index,
                            observedAtUtc),
                        transaction,
                        cancellationToken: cancellationToken));
            }

            stopwatch.Stop();
            if (affected != MutationBatchSize)
            {
                throw new InvalidOperationException(
                    $"MySQL trigger_insert 期望写入 "
                    + $"{MutationBatchSize} 行，实际 {affected} 行。");
            }

            return stopwatch.Elapsed;
        }
        finally
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private async Task<TimeSpan> MeasureClaimAsync(
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var claimed = (await ClaimAsync(
                    connection,
                    transaction,
                    observedAtUtc,
                    cancellationToken))
                .Count;
            stopwatch.Stop();
            if (claimed != MutationBatchSize)
            {
                throw new InvalidOperationException(
                    $"MySQL claim 期望领取 {MutationBatchSize} 行，"
                    + $"实际 {claimed} 行。");
            }

            return stopwatch.Elapsed;
        }
        finally
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private async Task<TimeSpan> MeasureTerminalSuccessAsync(
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var leaseId = Guid.CreateVersion7();
            var claimed = await ClaimAsync(
                connection,
                transaction,
                observedAtUtc,
                cancellationToken,
                leaseId);
            var stopwatch = Stopwatch.StartNew();
            var affected = 0;
            foreach (var id in claimed)
            {
                affected += await connection.ExecuteAsync(
                    new CommandDefinition(
                        JobsBacklogMutationSql
                            .ForProvider(ProviderName)
                            .TerminalSuccessSql,
                        new
                        {
                            Id = id,
                            LeaseId = leaseId,
                            RunningStatus =
                                JobExecutionStatuses.Running,
                            SucceededStatus =
                                JobExecutionStatuses.Succeeded,
                            FinishedAtUtc = observedAtUtc.UtcDateTime,
                        },
                        transaction,
                        cancellationToken: cancellationToken));
            }

            stopwatch.Stop();
            if (affected != MutationBatchSize)
            {
                throw new InvalidOperationException(
                    $"MySQL terminal_success 期望更新 "
                    + $"{MutationBatchSize} 行，实际 {affected} 行。");
            }

            return stopwatch.Elapsed;
        }
        finally
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private async Task<IReadOnlyList<Guid>> ClaimAsync(
        MySqlConnection connection,
        DbTransaction transaction,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken,
        Guid? fixedLeaseId = null)
    {
        var leaseId = fixedLeaseId ?? Guid.CreateVersion7();
        var statements = JobsBacklogMutationSql.ForProvider(ProviderName);
        var ids = (await connection.QueryAsync<Guid>(
                new CommandDefinition(
                    statements.ClaimSelectSql,
                    new
                    {
                        BatchSize = MutationBatchSize,
                        PendingStatus =
                            JobExecutionStatuses.Pending,
                        RunningStatus =
                            JobExecutionStatuses.Running,
                        Now = observedAtUtc.UtcDateTime,
                    },
                    transaction,
                    commandTimeout: 120,
                    cancellationToken: cancellationToken)))
            .ToArray();
        if (ids.Length == 0)
        {
            return ids;
        }

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                statements.ClaimUpdateSql
                    ?? throw new InvalidOperationException(
                        "MySQL claim update Statement 缺失。"),
                new
                {
                    Ids = ids,
                    RunningStatus = JobExecutionStatuses.Running,
                    LeaseId = leaseId,
                    LeaseExpiresAtUtc =
                        observedAtUtc.AddMinutes(5).UtcDateTime,
                    Now = observedAtUtc.UtcDateTime,
                },
                transaction,
                commandTimeout: 120,
                cancellationToken: cancellationToken));
        if (affected != ids.Length)
        {
            throw new InvalidOperationException(
                $"MySQL claim 选中 {ids.Length} 行，更新 {affected} 行。");
        }

        return ids;
    }

    private static DateTimeOffset? AsUtc(DateTime? value) =>
        value is { } dateTime
            ? new DateTimeOffset(
                DateTime.SpecifyKind(dateTime, DateTimeKind.Utc))
            : null;

    private sealed class MySqlBacklogRow
    {
        public long PendingCount { get; init; }

        public DateTime? OldestClaimableCreatedAtUtc { get; init; }

        public long DueRetryCount { get; init; }

        public DateTime? OldestDueRetryAtUtc { get; init; }
    }

    private const int MutationBatchSize = 20;
}
