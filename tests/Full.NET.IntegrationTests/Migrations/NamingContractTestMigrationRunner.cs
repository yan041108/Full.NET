using DbUp;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

internal static class NamingContractTestMigrationRunner
{
    public static async Task PrepareMySqlExpandStateAsync(string connectionString)
    {
        await NamingExpandTestMigrationRunner.MigrateMySqlThrough009Async(connectionString);
        await using var connection = new MySqlConnector.MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        await connection.OpenAsync();
        await NamingExpandTestData.InsertTenantAndOutboxAsync(connection);
        await NamingExpandTestMigrationRunner.MigrateMySqlThrough010Async(connectionString);
    }

    public static async Task PrepareSqlServerExpandStateAsync(string connectionString)
    {
        await NamingExpandTestMigrationRunner.MigrateSqlServerThrough009Async(connectionString);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await NamingExpandTestData.InsertTenantAndOutboxAsync(connection);
        await NamingExpandTestMigrationRunner.MigrateSqlServerThrough010Async(connectionString);
    }

    public static IDatabaseMigrationRunner CreateMySqlRunner(string connectionString) =>
        new Through011MigrationRunner(
            cancellationToken => MigrateMySqlThrough011Async(
                connectionString,
                cancellationToken));

    public static IDatabaseMigrationRunner CreateSqlServerRunner(string connectionString) =>
        new Through011MigrationRunner(
            cancellationToken => MigrateSqlServerThrough011Async(
                connectionString,
                cancellationToken));

    private static Task<MigrationResult> MigrateMySqlThrough011Async(
        string connectionString,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = DeployChanges.To.MySqlDatabase(
                MySqlConnectionStringPolicy.Create(
                    connectionString,
                    MySqlGuidStorageMode.Binary16,
                    allowUserVariables: true))
            .WithScriptsEmbeddedInAssembly(
                typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.MySql.", StringComparison.Ordinal)
                    && NamingExpandTestMigrationRunner.IsThroughMigration(name, 11))
            .WithVariable("UuidContractMaintenanceMode", "1")
            .WithVariable("UuidContractBackupVerified", "1")
            .WithVariable("UuidContractLegacyWritersStopped", "1")
            .WithVariable("UuidContractDestructiveDdlApprovalId", "test-uuid-contract-009")
            .WithVariable("PreV1NamingContractMaintenanceMode", "1")
            .WithVariable("PreV1NamingContractBackupVerified", "1")
            .WithVariable("PreV1NamingContractLegacyWritersStopped", "1")
            .WithVariable("PreV1NamingContractLegacyOutboxDrained", "1")
            .WithVariable(
                "PreV1NamingContractDestructiveDdlApprovalId",
                MigrationContractOptionFactory.NamingApprovalId)
            .WithExecutionTimeout(TimeSpan.FromSeconds(300))
            .Build()
            .PerformUpgrade();
        return ToMigrationResult(result);
    }

    private static Task<MigrationResult> MigrateSqlServerThrough011Async(
        string connectionString,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = DeployChanges.To.SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.SqlServer.", StringComparison.Ordinal)
                    && NamingExpandTestMigrationRunner.IsThroughMigration(name, 11))
            .WithVariable("UuidContractMaintenanceMode", "1")
            .WithVariable("UuidContractBackupVerified", "1")
            .WithVariable("UuidContractLegacyWritersStopped", "1")
            .WithVariable("UuidContractDestructiveDdlApprovalId", "test-uuid-contract-009")
            .WithVariable("PreV1NamingContractMaintenanceMode", "1")
            .WithVariable("PreV1NamingContractBackupVerified", "1")
            .WithVariable("PreV1NamingContractLegacyWritersStopped", "1")
            .WithVariable("PreV1NamingContractLegacyOutboxDrained", "1")
            .WithVariable(
                "PreV1NamingContractDestructiveDdlApprovalId",
                MigrationContractOptionFactory.NamingApprovalId)
            .WithExecutionTimeout(TimeSpan.FromSeconds(300))
            .Build()
            .PerformUpgrade();
        return ToMigrationResult(result);
    }

    private static Task<MigrationResult> ToMigrationResult(
        DbUp.Engine.DatabaseUpgradeResult result)
    {
        if (!result.Successful)
        {
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        return Task.FromResult(new MigrationResult(true, result.Scripts.Count()));
    }

    private sealed class Through011MigrationRunner(
        Func<CancellationToken, Task<MigrationResult>> migrate)
        : IDatabaseMigrationRunner
    {
        public Task<MigrationResult> MigrateAsync(
            CancellationToken cancellationToken = default) =>
            migrate(cancellationToken);
    }
}
