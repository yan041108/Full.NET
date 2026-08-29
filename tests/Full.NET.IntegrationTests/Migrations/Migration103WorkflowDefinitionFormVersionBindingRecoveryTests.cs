using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 103 在双库中建立不可空表单版本绑定及模块内外键。</summary>
[TestClass]
public sealed class Migration103WorkflowDefinitionFormVersionBindingRecoveryTests
{
    [TestMethod]
    public async Task SqlServer_creates_required_form_version_binding_and_is_idempotent()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);

        await runner.MigrateAsync();
        await using var connection = new SqlConnection(connectionString);

        Assert.AreEqual(
            0,
            await connection.ExecuteScalarAsync<int>(
                "SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_definition_version') AND name = N'FormVersionId'"));
        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.fn_workflow_definition_version') AND name = N'FK_fn_workflow_definition_version_FormVersion'"));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_creates_required_form_version_binding_and_is_idempotent()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);

        await runner.MigrateAsync();
        await using var connection = new MySqlConnection(connectionString);

        Assert.AreEqual(
            "NO",
            await connection.ExecuteScalarAsync<string>(
                "SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_definition_version' AND COLUMN_NAME = 'FormVersionId'"));
        Assert.AreEqual(
            1,
            await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_definition_version' AND CONSTRAINT_NAME = 'FK_fn_workflow_definition_version_FormVersion'"));
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    private static DbUpMigrationRunner CreateRunner(DatabaseProvider provider, string connectionString) =>
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
