using Dapper;
using DbUp;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Testcontainers.MsSql;
using Testcontainers.MySql;

namespace Full.NET.IntegrationTests.Migrations;

[TestClass]
public sealed class UuidBinaryContractRecoveryTests
{
    private readonly MySqlContainer _container = new MySqlBuilder("mysql:8.0")
        .WithCommand("--log-bin-trust-function-creators=1")
        .WithDatabase("fullnet")
        .WithUsername("fullnet")
        .WithPassword("FullNet_Test!123")
        .Build();

    [TestInitialize]
    public Task StartAsync() => _container.StartAsync();

    [TestCleanup]
    public async Task CleanupAsync() => await _container.DisposeAsync();

    [TestMethod]
    public async Task UuidBinaryContractRecovery_MySql_recovers_partial_constraint_deletion()
    {
        var runner = CreateMySqlRunner();
        await runner.MigrateAsync();
        await using var connection = CreateMySqlConnection();
        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_seed_run_item DROP FOREIGN KEY FK_fn_seed_run_item_Run;
            ALTER TABLE fn_seed_run_item DROP PRIMARY KEY;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%009_UuidBinaryContract.sql';
            """);

        var recovery = await runner.MigrateAsync();

        Assert.AreEqual(1, recovery.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = DATABASE()
              AND CONSTRAINT_NAME = 'FK_fn_seed_run_item_Run'
            """));
    }

    [TestMethod]
    public async Task UuidBinaryContractRecovery_MySql_recovers_partial_column_rename()
    {
        await ApplyExpandAsync();
        await using var connection = CreateMySqlConnection();
        await connection.ExecuteAsync(
            """
            CREATE TABLE fn_uuid_contract_state
            (
                Id tinyint NOT NULL,
                SchemaMode varchar(16) NOT NULL,
                DestructiveDdlApprovalId varchar(64) NOT NULL,
                UpdatedAtUtc datetime(6) NOT NULL,
                PRIMARY KEY (Id)
            );
            INSERT INTO fn_uuid_contract_state
                (Id, SchemaMode, DestructiveDdlApprovalId, UpdatedAtUtc)
            VALUES (1, 'Contracting', 'test-uuid-contract-009', UTC_TIMESTAMP(6));
            ALTER TABLE fn_outbox_message RENAME COLUMN LockId TO LockIdLegacy;
            ALTER TABLE fn_outbox_message RENAME COLUMN LockIdBinary TO LockId;
            """);

        var recovery = await CreateMySqlRunner().MigrateAsync();

        Assert.AreEqual(1, recovery.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_outbox_message'
              AND COLUMN_NAME = 'LockId' AND DATA_TYPE = 'binary'
              AND CHARACTER_MAXIMUM_LENGTH = 16 AND IS_NULLABLE = 'YES'
            """));
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_uuid_contract_state'
              AND CONSTRAINT_NAME IN
                  ('CK_fn_uuid_contract_state_Id', 'CK_fn_uuid_contract_state_SchemaMode')
              AND CONSTRAINT_TYPE = 'CHECK'
            """));
    }

    [TestMethod]
    public async Task UuidBinaryContractRecovery_SqlServer_recovers_unjournaled_index_state()
    {
        await using var container = new MsSqlBuilder(
                "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword("FullNet_Test!123")
            .Build();
        await container.StartAsync();
        var runner = CreateSqlServerRunner(container.GetConnectionString());
        await runner.MigrateAsync();
        await using var connection = new SqlConnection(container.GetConnectionString());
        await connection.ExecuteAsync(
            """
            ALTER TABLE dbo.fn_seed_run_item DROP CONSTRAINT FK_fn_seed_run_item_Run;
            ALTER TABLE dbo.fn_seed_run_item DROP CONSTRAINT PK_fn_seed_run_item;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%009_UuidBinaryContract.sql';
            """);

        var recovery = await runner.MigrateAsync();

        Assert.AreEqual(1, recovery.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.key_constraints WHERE name = 'PK_fn_seed_run_item'"));
    }

    [TestMethod]
    [DataRow(MySqlGuidStorageMode.Binary16, false)]
    [DataRow(MySqlGuidStorageMode.LegacyChar36, true)]
    public async Task UuidBinaryContractRecovery_application_rejects_schema_mode_mismatch(
        MySqlGuidStorageMode applicationMode,
        bool applyContract)
    {
        if (applyContract)
        {
            await CreateMySqlRunner().MigrateAsync();
        }
        else
        {
            await ApplyExpandAsync();
        }

        var settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "MySql",
            ["Database:ConnectionString"] = _container.GetConnectionString(),
            ["Database:MySqlGuidStorageMode"] = applicationMode.ToString(),
            ["Database:CommandTimeoutSeconds"] = "30",
        };
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddFullNetDapper(builder.Configuration, "Testing");
        builder.Services.AddFullNetDatabaseSchemaModeGuard();
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
        StringAssert.Contains(exception.Message,
            "MySQL UUID 应用模式与数据库 Contract schema 状态不一致");
    }

    [TestMethod]
    public async Task UuidBinaryContractRecovery_Production_rejects_legacy_application_mode()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "MySql",
            ["Database:ConnectionString"] = _container.GetConnectionString(),
            ["Database:MySqlGuidStorageMode"] = MySqlGuidStorageMode.LegacyChar36.ToString(),
            ["Database:CommandTimeoutSeconds"] = "30",
        };
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddFullNetDapper(builder.Configuration, Environments.Production);
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        StringAssert.Contains(exception.Message,
            "LegacyChar36 is not permitted in Production");
    }

    private DbUpMigrationRunner CreateMySqlRunner() => new(
        Options.Create(new DatabaseOptions
        {
            Provider = DatabaseProvider.MySql,
            ConnectionString = _container.GetConnectionString(),
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 300,
        }),
        NullLoggerFactory.Instance,
        ContractOptions());

    private static DbUpMigrationRunner CreateSqlServerRunner(string connectionString) => new(
        Options.Create(new DatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = connectionString,
            CommandTimeoutSeconds = 300,
        }),
        NullLoggerFactory.Instance,
        ContractOptions());

    private static IOptions<UuidBinaryContractOptions> ContractOptions() =>
        Options.Create(new UuidBinaryContractOptions
        {
            MaintenanceMode = true,
            BackupVerified = true,
            LegacyWritersStopped = true,
            DestructiveDdlApprovalId = "test-uuid-contract-009",
        });

    private async Task ApplyExpandAsync()
    {
        var result = DeployChanges.To.MySqlDatabase(
                MySqlConnectionStringPolicy.Create(
                    _container.GetConnectionString(),
                    MySqlGuidStorageMode.LegacyChar36,
                    allowUserVariables: true))
            .WithScriptsEmbeddedInAssembly(
                typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(".Migrations.MySql.", StringComparison.Ordinal)
                    && !name.EndsWith("009_UuidBinaryContract.sql", StringComparison.Ordinal))
            .Build()
            .PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.ToString());
        await Task.CompletedTask;
    }

    private MySqlConnection CreateMySqlConnection() => new(
        MySqlConnectionStringPolicy.Create(
            _container.GetConnectionString(),
            MySqlGuidStorageMode.Binary16,
            allowUserVariables: false));
}
