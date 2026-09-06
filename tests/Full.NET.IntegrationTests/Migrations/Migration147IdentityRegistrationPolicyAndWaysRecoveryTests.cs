using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 147 在注册策略与注册方式表已存在时仍可幂等重跑。</summary>
[TestClass]
public sealed class Migration147IdentityRegistrationPolicyAndWaysRecoveryTests
{
    [TestMethod]
    public async Task SqlServer_registration_tables_migration_is_idempotent()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        Assert.AreEqual(1, await TableExistsAsync(connection, "fn_identity_registration_policy", true));
        Assert.AreEqual(1, await TableExistsAsync(connection, "fn_identity_user_registration_way", true));
        Assert.AreEqual(1, await PolicySeedExistsAsync(connection, true));

        await DeleteMigrationRecordAsync(connection, true);
        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await PolicySeedExistsAsync(connection, true));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_registration_tables_migration_is_idempotent()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();

        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        Assert.AreEqual(1, await TableExistsAsync(connection, "fn_identity_registration_policy", false));
        Assert.AreEqual(1, await TableExistsAsync(connection, "fn_identity_user_registration_way", false));
        Assert.AreEqual(1, await PolicySeedExistsAsync(connection, false));

        await DeleteMigrationRecordAsync(connection, false);
        var recovered = await runner.MigrateAsync();
        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await PolicySeedExistsAsync(connection, false));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    private static Task DeleteMigrationRecordAsync(
        System.Data.Common.DbConnection connection,
        bool isSqlServer) =>
        connection.ExecuteAsync(
            isSqlServer
                ? """
                  DELETE FROM dbo.SchemaVersions
                  WHERE ScriptName LIKE '%147_IdentityRegistrationPolicyAndWays.sql';
                  """
                : """
                  DELETE FROM schemaversions
                  WHERE ScriptName LIKE '%147_IdentityRegistrationPolicyAndWays.sql';
                  """);

    private static Task<int> TableExistsAsync(
        System.Data.Common.DbConnection connection,
        string tableName,
        bool isSqlServer) =>
        connection.ExecuteScalarAsync<int>(
            isSqlServer
                ? """
                  SELECT COUNT(*)
                  FROM sys.tables
                  WHERE name = @TableName
                  """
                : """
                  SELECT COUNT(*)
                  FROM information_schema.tables
                  WHERE table_schema = DATABASE()
                    AND table_name = @TableName
                  """,
            new { TableName = tableName });

    private static Task<int> PolicySeedExistsAsync(
        System.Data.Common.DbConnection connection,
        bool isSqlServer) =>
        connection.ExecuteScalarAsync<int>(
            isSqlServer
                ? """
                  SELECT COUNT(*)
                  FROM dbo.fn_identity_registration_policy
                  WHERE Id = '00000000-0000-4000-8000-000000000001'
                  """
                : """
                  SELECT COUNT(*)
                  FROM fn_identity_registration_policy
                  WHERE Id = UUID_TO_BIN('00000000-0000-4000-8000-000000000001', 0)
                  """);

    private static DbUpMigrationRunner CreateRunner(
        DatabaseProvider provider,
        string connectionString) =>
        new(
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
}
