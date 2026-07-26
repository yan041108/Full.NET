using DbUp;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;

namespace Full.NET.IntegrationTests.Migrations;

internal sealed class UuidBinaryExpandTestMigrationRunner(string connectionString)
    : IDatabaseMigrationRunner
{
    public Task<MigrationResult> MigrateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = DeployChanges.To.MySqlDatabase(
                MySqlConnectionStringPolicy.Create(
                    connectionString,
                    MySqlGuidStorageMode.LegacyChar36,
                    allowUserVariables: true))
            .WithScriptsEmbeddedInAssembly(
                typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.MySql.", StringComparison.Ordinal)
                    && IsThrough008Migration(name))
            .Build()
            .PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        return Task.FromResult(new MigrationResult(true, result.Scripts.Count()));
    }

    internal static bool IsThrough008Migration(string resourceName) =>
        Enumerable.Range(1, 8).Any(number =>
            resourceName.Contains($".{number:000}_", StringComparison.Ordinal));
}
