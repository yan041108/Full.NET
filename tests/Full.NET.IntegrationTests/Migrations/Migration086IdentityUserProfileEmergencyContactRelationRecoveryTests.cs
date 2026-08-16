using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>确认 086 从 UTF-16 纠正为 UTF-8 后可幂等应用。</summary>
[TestClass]
public sealed class Migration086IdentityUserProfileEmergencyContactRelationRecoveryTests
{
    [TestMethod]
    public Task MySql_applies_emergency_contact_relation_idempotently() =>
        VerifyAsync(DatabaseProvider.MySql);

    [TestMethod]
    public Task SqlServer_applies_emergency_contact_relation_idempotently() =>
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
