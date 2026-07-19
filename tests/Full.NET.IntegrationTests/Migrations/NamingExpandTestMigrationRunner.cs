using DbUp;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;

namespace Full.NET.IntegrationTests.Migrations;

internal static class NamingExpandTestMigrationRunner
{
    private const string ContractApprovalId = "test-uuid-contract-009";

    public static Task<MigrationResult> MigrateMySqlThrough009Async(
        string connectionString,
        CancellationToken cancellationToken = default)
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
                    && !name.EndsWith("010_NamingExpand.sql", StringComparison.Ordinal)
                    && !name.EndsWith("011_NamingContract.sql", StringComparison.Ordinal))
            .WithVariable("UuidContractMaintenanceMode", "1")
            .WithVariable("UuidContractBackupVerified", "1")
            .WithVariable("UuidContractLegacyWritersStopped", "1")
            .WithVariable("UuidContractDestructiveDdlApprovalId", ContractApprovalId)
            .WithExecutionTimeout(TimeSpan.FromSeconds(300))
            .Build()
            .PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        return Task.FromResult(new MigrationResult(true, result.Scripts.Count()));
    }

    public static Task<MigrationResult> MigrateSqlServerThrough009Async(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = DeployChanges.To.SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.SqlServer.", StringComparison.Ordinal)
                    && !name.EndsWith("010_NamingExpand.sql", StringComparison.Ordinal)
                    && !name.EndsWith("011_NamingContract.sql", StringComparison.Ordinal))
            .WithVariable("UuidContractMaintenanceMode", "1")
            .WithVariable("UuidContractBackupVerified", "1")
            .WithVariable("UuidContractLegacyWritersStopped", "1")
            .WithVariable("UuidContractDestructiveDdlApprovalId", ContractApprovalId)
            .WithExecutionTimeout(TimeSpan.FromSeconds(300))
            .Build()
            .PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        return Task.FromResult(new MigrationResult(true, result.Scripts.Count()));
    }
}
