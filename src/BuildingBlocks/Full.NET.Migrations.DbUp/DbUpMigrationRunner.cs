using DbUp;
using DbUp.Builder;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Migrations.DbUp;

public sealed class DbUpMigrationRunner(
    IOptions<DatabaseOptions> databaseOptions,
    ILoggerFactory loggerFactory) : IDatabaseMigrationRunner
{
    public Task<MigrationResult> MigrateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = databaseOptions.Value;
        var (builder, providerSegment) = CreateBuilder(options);
        var upgrader = builder
            .WithScriptsEmbeddedInAssembly(
                MigrationAssembly.Value,
                name => name.Contains(providerSegment, StringComparison.Ordinal))
            .LogTo(loggerFactory)
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        return Task.FromResult(new MigrationResult(true, result.Scripts.Count()));
    }

    private static (UpgradeEngineBuilder Builder, string ProviderSegment) CreateBuilder(
        DatabaseOptions options)
    {
        switch (options.Provider)
        {
            case DatabaseProvider.SqlServer:
                return (
                    DeployChanges.To.SqlDatabase(options.ConnectionString),
                    ".Migrations.SqlServer.");

            case DatabaseProvider.MySql:
                return (
                    DeployChanges.To.MySqlDatabase(options.ConnectionString),
                    ".Migrations.MySql.");

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    options.Provider,
                    "Unsupported database provider.");
        }
    }
}
