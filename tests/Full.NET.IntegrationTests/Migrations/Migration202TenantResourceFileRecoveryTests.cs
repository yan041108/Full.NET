using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 202 租户资源文件表在删除后可幂等恢复，且不引入跨模块外键。</summary>
[TestClass]
public sealed class Migration202TenantResourceFileRecoveryTests
{
    private const string TableName = "fn_files_tenant_resource_file";
    private const string ScriptToken = "202_TenantResourceFile.sql";

    /// <summary>SQL Server 删除表后重跑 202 必须重建所有权表。</summary>
    [TestMethod]
    public async Task SqlServer_recovers_missing_tenant_resource_file_table()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync().ConfigureAwait(false);
        var runner = ReviewFixMigrationRecoverySupport.CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync().ConfigureAwait(false);
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync($"DROP TABLE dbo.{TableName};").ConfigureAwait(false);
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, ScriptToken, sqlServer: true)
            .ConfigureAwait(false);
        Assert.AreEqual(1, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.TableExistsAsync(connection, TableName, sqlServer: true)
            .ConfigureAwait(false));
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM sys.foreign_keys
            WHERE parent_object_id = OBJECT_ID(N'dbo.fn_files_tenant_resource_file')
            """).ConfigureAwait(false));
        Assert.AreEqual(0, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
    }

    /// <summary>MySQL 删除表后重跑 202 必须重建所有权表。</summary>
    [TestMethod]
    public async Task MySql_recovers_missing_tenant_resource_file_table()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync().ConfigureAwait(false);
        var runner = ReviewFixMigrationRecoverySupport.CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync().ConfigureAwait(false);
        await using var connection = ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        await connection.ExecuteAsync($"DROP TABLE {TableName};").ConfigureAwait(false);
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, ScriptToken, sqlServer: false)
            .ConfigureAwait(false);
        Assert.AreEqual(1, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.TableExistsAsync(connection, TableName, sqlServer: false)
            .ConfigureAwait(false));
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM information_schema.table_constraints
            WHERE table_schema = DATABASE()
              AND table_name = 'fn_files_tenant_resource_file'
              AND constraint_type = 'FOREIGN KEY'
            """).ConfigureAwait(false));
        Assert.AreEqual(0, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
    }
}
