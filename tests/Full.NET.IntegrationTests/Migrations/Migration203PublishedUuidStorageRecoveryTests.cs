using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证已发布 UUID 类型的前向修复、断点恢复和非法数据失败关闭；需要隔离双库环境。</summary>
[TestClass]
public sealed class Migration203PublishedUuidStorageRecoveryTests
{
    /// <summary>旧文本或转换中间态都恢复为相同 Guid，空租户值不变。</summary>
    /// <param name="interrupted">是否模拟已转换字节、尚未收敛列宽的中间态。</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MySql_upgrade_preserves_guid_and_nullable_scope_async(bool interrupted)
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync().ConfigureAwait(false);
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync().ConfigureAwait(false);
        await using var connection = MySqlConnection(connectionString);
        await connection.ExecuteAsync("ALTER TABLE fn_ai_model_config MODIFY COLUMN Id char(36) NOT NULL, MODIFY COLUMN TenantId char(36) NULL").ConfigureAwait(false);
        var id = Guid.CreateVersion7();
        await connection.ExecuteAsync(
            """
            INSERT INTO fn_ai_model_config (Id, TenantId, Name, ProviderKey, EndpointBaseUrl, ModelId, CreatedAtUtc)
            VALUES (@Id, NULL, 'upgrade', 'ollama', 'https://provider.test', 'test', UTC_TIMESTAMP(6));
            DELETE FROM schemaversions WHERE ScriptName LIKE '%203_PublishedModuleUuidStorage.sql';
            """, new { Id = id.ToString("D").ToUpperInvariant() }).ConfigureAwait(false);
        if (interrupted)
        {
            await connection.ExecuteAsync(
                """
                ALTER TABLE fn_ai_model_config MODIFY COLUMN Id varbinary(36) NOT NULL;
                UPDATE fn_ai_model_config SET Id = UNHEX(REPLACE(CONVERT(Id USING ascii), '-', ''));
                """).ConfigureAwait(false);
        }
        Assert.AreEqual(1, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
        Assert.AreEqual(id, await connection.QuerySingleAsync<Guid>("SELECT Id FROM fn_ai_model_config WHERE Id = @Id", new { Id = id }).ConfigureAwait(false));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_model_config WHERE TenantId IS NULL").ConfigureAwait(false));
        Assert.AreEqual(16, await connection.ExecuteScalarAsync<int>("SELECT OCTET_LENGTH(Id) FROM fn_ai_model_config").ConfigureAwait(false));
        Assert.AreEqual(0, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
    }

    /// <summary>非法 UUID 在修改业务列之前中止，原文仍可供人工修复。</summary>
    [TestMethod]
    public async Task MySql_invalid_uuid_fails_before_business_ddl_async()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync().ConfigureAwait(false);
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync().ConfigureAwait(false);
        await using var connection = MySqlConnection(connectionString);
        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_ai_model_config MODIFY COLUMN Id char(36) NOT NULL;
            INSERT INTO fn_ai_model_config (Id, TenantId, Name, ProviderKey, EndpointBaseUrl, ModelId, CreatedAtUtc)
            VALUES ('invalid-uuid', NULL, 'invalid', 'ollama', 'https://provider.test', 'test', UTC_TIMESTAMP(6));
            DELETE FROM schemaversions WHERE ScriptName LIKE '%203_PublishedModuleUuidStorage.sql';
            """).ConfigureAwait(false);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => runner.MigrateAsync());
        Assert.AreEqual("invalid-uuid", await connection.QuerySingleAsync<string>("SELECT Id FROM fn_ai_model_config").ConfigureAwait(false));
        Assert.AreEqual("char", await connection.QuerySingleAsync<string>(
            "SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_model_config' AND COLUMN_NAME = 'Id'").ConfigureAwait(false));
    }

    /// <summary>SQL Server 配对脚本仅检查类型，重新执行不改变 Guid。</summary>
    [TestMethod]
    public async Task SqlServer_forward_validation_preserves_guid_async()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync().ConfigureAwait(false);
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync().ConfigureAwait(false);
        await using var connection = new SqlConnection(connectionString);
        var id = Guid.CreateVersion7();
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.fn_ai_model_config (Id, TenantId, Name, ProviderKey, EndpointBaseUrl, ModelId, CreatedAtUtc)
            VALUES (@Id, NULL, N'upgrade', 'ollama', 'https://provider.test', 'test', SYSUTCDATETIME());
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%203_PublishedModuleUuidStorage.sql';
            """, new { Id = id }).ConfigureAwait(false);
        Assert.AreEqual(1, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
        Assert.AreEqual(id, await connection.QuerySingleAsync<Guid>("SELECT Id FROM dbo.fn_ai_model_config WHERE Id = @Id", new { Id = id }).ConfigureAwait(false));
        Assert.AreEqual(0, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
    }

    /// <summary>创建使用标准 Binary16 Guid 字节序的测试连接。</summary>
    /// <param name="connectionString">隔离测试库连接字符串。</param>
    private static MySqlConnection MySqlConnection(string connectionString) => new(
        MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));

    /// <summary>构建使用正式 UUID 与命名策略的迁移执行器。</summary>
    /// <param name="provider">数据库提供程序。</param>
    /// <param name="connectionString">隔离测试库连接字符串。</param>
    private static DbUpMigrationRunner CreateRunner(DatabaseProvider provider, string connectionString) => new(
        Options.Create(new DatabaseOptions
        {
            Provider = provider, ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16, CommandTimeoutSeconds = 300,
        }), NullLoggerFactory.Instance,
        MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions());
}
