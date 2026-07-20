using DbUp;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>
/// UUID 009 Contract 边界专用：跑到 009 为止，排除后续 naming 010/011，避免恢复用例撞上已 DROP 的 legacy 表。
/// </summary>
internal static class UuidBinaryContractTestMigrationRunner
{
    private const string ApprovalId = "test-uuid-contract-009";

    public static Task<MigrationResult> MigrateMySqlThrough009Async(string connectionString)
    {
        var result = DeployChanges.To.MySqlDatabase(
                MySqlConnectionStringPolicy.Create(
                    connectionString,
                    MySqlGuidStorageMode.Binary16,
                    allowUserVariables: true))
            .WithScriptsEmbeddedInAssembly(
                typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.MySql.", StringComparison.Ordinal)
                    && !name.EndsWith("010_NamingExpand.sql", StringComparison.Ordinal)
                    && !name.EndsWith("011_NamingContract.sql", StringComparison.Ordinal))
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

    public static Task<MigrationResult> MigrateSqlServerThrough009Async(string connectionString)
    {
        var result = DeployChanges.To.SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.SqlServer.", StringComparison.Ordinal)
                    && !name.EndsWith("010_NamingExpand.sql", StringComparison.Ordinal)
                    && !name.EndsWith("011_NamingContract.sql", StringComparison.Ordinal))
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

    public static DbUpMigrationRunner CreateMySqlRunner(string connectionString) => new(
        Options.Create(new DatabaseOptions
        {
            Provider = DatabaseProvider.MySql,
            ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 300,
        }),
        NullLoggerFactory.Instance,
        Options.Create(new UuidBinaryContractOptions
        {
            MaintenanceMode = true,
            BackupVerified = true,
            LegacyWritersStopped = true,
            DestructiveDdlApprovalId = ApprovalId,
        }),
        MigrationContractOptionFactory.NamingOptions());
}
