using DbUp;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;

namespace Full.NET.IntegrationTests.Migrations;

internal static class AuditingSanitizationTestMigrationRunner
{
    public static Task MigrateMySqlThrough031Async(string connectionString)
    {
        var result = DeployChanges.To.MySqlDatabase(
                MySqlConnectionStringPolicy.Create(
                    connectionString,
                    MySqlGuidStorageMode.Binary16,
                    allowUserVariables: true))
            .WithScriptsEmbeddedInAssembly(
                typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.MySql.", StringComparison.Ordinal)
                    && NamingExpandTestMigrationRunner.IsThroughMigration(name, 31))
            .WithVariables(MigrationVariables())
            .WithExecutionTimeout(TimeSpan.FromSeconds(300))
            .Build()
            .PerformUpgrade();
        EnsureSuccessful(result);
        return Task.CompletedTask;
    }

    public static Task MigrateSqlServerThrough031Async(string connectionString)
    {
        var result = DeployChanges.To.SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.SqlServer.", StringComparison.Ordinal)
                    && NamingExpandTestMigrationRunner.IsThroughMigration(name, 31))
            .WithVariables(MigrationVariables())
            .WithExecutionTimeout(TimeSpan.FromSeconds(300))
            .Build()
            .PerformUpgrade();
        EnsureSuccessful(result);
        return Task.CompletedTask;
    }

    private static Dictionary<string, string> MigrationVariables() =>
        new(StringComparer.Ordinal)
        {
            ["UuidContractMaintenanceMode"] = "1",
            ["UuidContractBackupVerified"] = "1",
            ["UuidContractLegacyWritersStopped"] = "1",
            ["UuidContractDestructiveDdlApprovalId"] = "test-uuid-contract-009",
            ["PreV1NamingContractMaintenanceMode"] = "1",
            ["PreV1NamingContractBackupVerified"] = "1",
            ["PreV1NamingContractLegacyWritersStopped"] = "1",
            ["PreV1NamingContractLegacyOutboxDrained"] = "1",
            ["PreV1NamingContractDestructiveDdlApprovalId"] =
                MigrationContractOptionFactory.NamingApprovalId,
        };

    private static void EnsureSuccessful(DbUp.Engine.DatabaseUpgradeResult result)
    {
        if (!result.Successful)
        {
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }
    }
}
