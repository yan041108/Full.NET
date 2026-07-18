using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Full.NET.Seeding.Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Testcontainers.MsSql;
using Testcontainers.MySql;

namespace Full.NET.IntegrationTests.Seeding;

[TestClass]
public sealed class SeedExecutionInfrastructureTests
{
    [TestMethod]
    public async Task SqlServer_lease_and_audit_store_are_database_backed()
    {
        await using var container = new MsSqlBuilder(
                "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword("FullNet_Test!123")
            .Build();
        await container.StartAsync();
        var options = new DatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = container.GetConnectionString(),
            CommandTimeoutSeconds = 300,
        };

        await VerifyInfrastructureAsync(
            options,
            () => new SqlConnection(container.GetConnectionString()));
    }

    [TestMethod]
    public async Task MySql_lease_and_audit_store_are_database_backed()
    {
        await using var container = new MySqlBuilder("mysql:8.0")
            .WithCommand("--log-bin-trust-function-creators=1")
            .WithDatabase("fullnet")
            .WithUsername("fullnet")
            .WithPassword("FullNet_Test!123")
            .Build();
        await container.StartAsync();
        var options = new DatabaseOptions
        {
            Provider = DatabaseProvider.MySql,
            ConnectionString = container.GetConnectionString(),
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 300,
        };

        await VerifyInfrastructureAsync(
            options,
            () => new MySqlConnection(
                MySqlConnectionStringPolicy.Create(
                    container.GetConnectionString(),
                    MySqlGuidStorageMode.Binary16,
                    allowUserVariables: false)));
    }

    private static async Task VerifyInfrastructureAsync(
        DatabaseOptions databaseOptions,
        Func<DbConnection> createConnection)
    {
        var migration = new DbUpMigrationRunner(
            Options.Create(databaseOptions),
            NullLoggerFactory.Instance,
            Options.Create(new UuidBinaryContractOptions
            {
                MaintenanceMode = true,
                BackupVerified = true,
                LegacyWritersStopped = true,
                DestructiveDdlApprovalId = "test-seeding-uuid-contract-009",
            }));
        var migrationResult = await migration.MigrateAsync();
        Assert.IsTrue(migrationResult.Successful);

        var seedOptions = Options.Create(new SeedOptions { LockTimeoutSeconds = 1 });
        var firstProvider = new SeedExecutionLease(
            Options.Create(databaseOptions),
            seedOptions);
        var competingProvider = new SeedExecutionLease(
            Options.Create(databaseOptions),
            seedOptions);
        await using (await firstProvider.AcquireAsync(CancellationToken.None))
        {
            var exception = await CaptureLeaseFailureAsync(competingProvider);
            Assert.AreEqual(SeedErrorCodes.LockTimeout, exception.Code);
        }

        await using (await competingProvider.AcquireAsync(CancellationToken.None))
        {
            // 释放首个会话锁后必须可以重新获取同名资源锁。
        }

        var runId = Guid.Parse("019822d3-0700-7000-8000-000000000101");
        var startedAt = new DateTimeOffset(2026, 7, 18, 8, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddSeconds(1);
        var store = new SeedExecutionStore(Options.Create(databaseOptions));
        await store.StartRunAsync(
            new SeedRunAuditStart(
                runId,
                "baseline",
                "IntegrationTests",
                "1.0.0",
                "trace-001",
                startedAt),
            CancellationToken.None);
        await store.StartItemAsync(
            new SeedRunItemAuditStart(runId, "tenancy.host", 1, startedAt),
            CancellationToken.None);
        await store.CompleteItemAsync(
            new SeedRunItemAuditCompletion(
                runId,
                "tenancy.host",
                SeedExecutionStatuses.Succeeded,
                1,
                2,
                3,
                null,
                completedAt),
            CancellationToken.None);
        await store.CompleteRunAsync(
            new SeedRunAuditCompletion(
                runId,
                SeedExecutionStatuses.Succeeded,
                null,
                completedAt),
            CancellationToken.None);

        await using var connection = createConnection();
        var run = await connection.QuerySingleAsync<RunAuditRow>(
            "SELECT Profile, EnvironmentName, Status, ErrorCode FROM fn_seed_run WHERE Id = @RunId",
            new { RunId = runId });
        Assert.AreEqual("baseline", run.Profile);
        Assert.AreEqual("IntegrationTests", run.EnvironmentName);
        Assert.AreEqual(SeedExecutionStatuses.Succeeded, run.Status);
        Assert.IsNull(run.ErrorCode);

        var item = await connection.QuerySingleAsync<ItemAuditRow>(
            """
            SELECT Contributor, Status, CreatedCount, UpdatedCount, SkippedCount, ErrorCode
            FROM fn_seed_run_item
            WHERE RunId = @RunId AND Contributor = @Contributor
            """,
            new { RunId = runId, Contributor = "tenancy.host" });
        Assert.AreEqual("tenancy.host", item.Contributor);
        Assert.AreEqual(SeedExecutionStatuses.Succeeded, item.Status);
        Assert.AreEqual(1, item.CreatedCount);
        Assert.AreEqual(2, item.UpdatedCount);
        Assert.AreEqual(3, item.SkippedCount);
        Assert.IsNull(item.ErrorCode);
    }

    private static async Task<SeedExecutionException> CaptureLeaseFailureAsync(
        SeedExecutionLease provider)
    {
        try
        {
            await using var unexpected = await provider.AcquireAsync(CancellationToken.None);
        }
        catch (SeedExecutionException exception)
        {
            return exception;
        }

        Assert.Fail("竞争会话不应在首个租约释放前取得 Seed 锁。");
        throw new InvalidOperationException();
    }

    private sealed record RunAuditRow(
        string Profile,
        string EnvironmentName,
        string Status,
        string? ErrorCode);

    private sealed record ItemAuditRow(
        string Contributor,
        string Status,
        int CreatedCount,
        int UpdatedCount,
        int SkippedCount,
        string? ErrorCode);
}
