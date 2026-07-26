using DbUp;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>
/// UUID 009 Contract 边界专用：跑到 009 为止，排除后续 naming 010/011，避免恢复用例撞上已 DROP 的 legacy 表。
/// </summary>
internal static class UuidBinaryContractTestMigrationRunner
{
    private const string ApprovalId = "test-uuid-contract-009";

    public static Task<MigrationResult> MigrateMySqlThrough009Async(
        string connectionString,
        bool maintenanceMode = true,
        bool backupVerified = true,
        bool legacyWritersStopped = true,
        string approvalId = ApprovalId)
    {
        var result = DeployChanges.To.MySqlDatabase(
                MySqlConnectionStringPolicy.Create(
                    connectionString,
                    MySqlGuidStorageMode.Binary16,
                    allowUserVariables: true))
            .WithScriptsEmbeddedInAssembly(
                typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.MySql.", StringComparison.Ordinal)
                    && NamingExpandTestMigrationRunner.IsThroughMigration(name, 9))
            .WithVariable("UuidContractMaintenanceMode", maintenanceMode ? "1" : "0")
            .WithVariable("UuidContractBackupVerified", backupVerified ? "1" : "0")
            .WithVariable("UuidContractLegacyWritersStopped", legacyWritersStopped ? "1" : "0")
            .WithVariable("UuidContractDestructiveDdlApprovalId", approvalId)
            .WithExecutionTimeout(TimeSpan.FromSeconds(300))
            .Build()
            .PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        return Task.FromResult(new MigrationResult(true, result.Scripts.Count()));
    }

    public static Task<MigrationResult> MigrateSqlServerThrough009Async(string connectionString)
    {
        var result = DeployChanges.To.SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.SqlServer.", StringComparison.Ordinal)
                    && NamingExpandTestMigrationRunner.IsThroughMigration(name, 9))
            .WithVariable("UuidContractMaintenanceMode", "1")
            .WithVariable("UuidContractBackupVerified", "1")
            .WithVariable("UuidContractLegacyWritersStopped", "1")
            .WithVariable("UuidContractDestructiveDdlApprovalId", ApprovalId)
            .WithExecutionTimeout(TimeSpan.FromSeconds(300))
            .Build()
            .PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        return Task.FromResult(new MigrationResult(true, result.Scripts.Count()));
    }

    public static IDatabaseMigrationRunner CreateMySqlRunner(
        string connectionString,
        bool maintenanceMode = true,
        bool backupVerified = true,
        bool legacyWritersStopped = true,
        string approvalId = ApprovalId) =>
        new Through009MigrationRunner(
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return MigrateMySqlThrough009Async(
                    connectionString,
                    maintenanceMode,
                    backupVerified,
                    legacyWritersStopped,
                    approvalId);
            });

    public static IDatabaseMigrationRunner CreateSqlServerRunner(string connectionString) =>
        new Through009MigrationRunner(
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return MigrateSqlServerThrough009Async(connectionString);
            });

    private sealed class Through009MigrationRunner(
        Func<CancellationToken, Task<MigrationResult>> migrate)
        : IDatabaseMigrationRunner
    {
        public Task<MigrationResult> MigrateAsync(
            CancellationToken cancellationToken = default) =>
            migrate(cancellationToken);
    }
}
