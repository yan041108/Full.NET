using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>确认 089 的 THEN/ALTER 粘连修复后可幂等应用。</summary>
[TestClass]
public sealed class Migration089JobsDefinitionAndScheduleEnhancementsRecoveryTests
{
    [TestMethod]
    public Task MySql_applies_jobs_definition_schedule_enhancements_idempotently() =>
        VerifyAsync(DatabaseProvider.MySql);

    [TestMethod]
    public Task SqlServer_applies_jobs_definition_schedule_enhancements_idempotently() =>
        VerifyAsync(DatabaseProvider.SqlServer);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var connectionString = provider == DatabaseProvider.MySql
            ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync()
            : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = new DbUpMigrationRunner(
            Options.Create(new DatabaseOptions
            {
                Provider = provider,
                ConnectionString = connectionString,
                MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
                CommandTimeoutSeconds = 300,
            }),
            NullLoggerFactory.Instance,
            MigrationContractOptionFactory.UuidOptions(),
            MigrationContractOptionFactory.NamingOptions());
        await runner.MigrateAsync();
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }
}
