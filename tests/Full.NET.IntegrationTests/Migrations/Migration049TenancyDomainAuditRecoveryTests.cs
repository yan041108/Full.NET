using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>
/// 验证 049 在 <c>fn_tenancy_domain_audit</c> 已存在但 TenantId 复合索引缺失或形状错误时能够无损恢复。
/// </summary>
[TestClass]
public sealed class Migration049TenancyDomainAuditRecoveryTests
{
    private const string IndexName =
        "IX_fn_tenancy_domain_audit_TenantId_OccurredAtUtc_Id";

    [TestMethod]
    public async Task SqlServer_domain_audit_migration_recovers_missing_index_without_dropping_data()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync();

        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(
            $"""
             INSERT INTO dbo.fn_tenancy_domain_audit
                 (Id, TenantId, ActionKey, EntityId, Outcome, ActorUserId,
                  ActorDisplayName, TraceId, DiffSummaryJson, OccurredAtUtc)
             VALUES
                 (NEWID(), NULL, 'tenancy.host_tenant.disable', NEWID(), 'success',
                  NULL, NULL, NULL, NULL, SYSDATETIMEOFFSET());
             DROP INDEX {IndexName} ON dbo.fn_tenancy_domain_audit;
             DELETE FROM dbo.SchemaVersions
             WHERE ScriptName LIKE '%049_TenancyDomainAudit.sql';
             """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM dbo.fn_tenancy_domain_audit
            WHERE ActionKey = 'tenancy.host_tenant.disable'
            """));
        await AssertSqlServerIndexShapeAsync(connection);

        await connection.ExecuteAsync(
            $"""
             DROP INDEX {IndexName} ON dbo.fn_tenancy_domain_audit;
             CREATE INDEX {IndexName}
                 ON dbo.fn_tenancy_domain_audit (OccurredAtUtc, Id);
             DELETE FROM dbo.SchemaVersions
             WHERE ScriptName LIKE '%049_TenancyDomainAudit.sql';
             """);

        var repaired = await runner.MigrateAsync();

        Assert.AreEqual(1, repaired.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM dbo.fn_tenancy_domain_audit
            WHERE ActionKey = 'tenancy.host_tenant.disable'
            """));
        await AssertSqlServerIndexShapeAsync(connection);
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_domain_audit_migration_recovers_missing_index_without_dropping_data()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();

        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        await connection.ExecuteAsync(
            $"""
             INSERT INTO fn_tenancy_domain_audit
                 (Id, TenantId, ActionKey, EntityId, Outcome, ActorUserId,
                  ActorDisplayName, TraceId, DiffSummaryJson, OccurredAtUtc)
             VALUES
                 (UNHEX(REPLACE(UUID(), '-', '')), NULL, 'tenancy.host_tenant.disable',
                  UNHEX(REPLACE(UUID(), '-', '')), 'success', NULL, NULL, NULL, NULL,
                  UTC_TIMESTAMP(6));
             DROP INDEX {IndexName} ON fn_tenancy_domain_audit;
             DELETE FROM schemaversions
             WHERE ScriptName LIKE '%049_TenancyDomainAudit.sql';
             """);

        var recovered = await runner.MigrateAsync();

        Assert.AreEqual(1, recovered.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM fn_tenancy_domain_audit
            WHERE ActionKey = 'tenancy.host_tenant.disable'
            """));
        await AssertMySqlIndexShapeAsync(connection);

        await connection.ExecuteAsync(
            $"""
             DROP INDEX {IndexName} ON fn_tenancy_domain_audit;
             CREATE INDEX {IndexName}
                 ON fn_tenancy_domain_audit (OccurredAtUtc, Id);
             DELETE FROM schemaversions
             WHERE ScriptName LIKE '%049_TenancyDomainAudit.sql';
             """);

        var repaired = await runner.MigrateAsync();

        Assert.AreEqual(1, repaired.ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM fn_tenancy_domain_audit
            WHERE ActionKey = 'tenancy.host_tenant.disable'
            """));
        await AssertMySqlIndexShapeAsync(connection);
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }

    private static async Task AssertSqlServerIndexShapeAsync(SqlConnection connection)
    {
        Assert.AreEqual(3, await connection.ExecuteScalarAsync<int>(
            $"""
             SELECT COUNT(*)
             FROM sys.indexes AS indexObject
             INNER JOIN sys.index_columns AS indexColumn
                 ON indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
             INNER JOIN sys.columns AS columnObject
                 ON columnObject.object_id = indexColumn.object_id
                AND columnObject.column_id = indexColumn.column_id
             WHERE indexObject.object_id =
                   OBJECT_ID(N'dbo.fn_tenancy_domain_audit')
               AND indexObject.name = N'{IndexName}'
               AND (
                    (indexColumn.key_ordinal = 1
                     AND columnObject.name = N'TenantId')
                    OR (indexColumn.key_ordinal = 2
                        AND columnObject.name = N'OccurredAtUtc')
                    OR (indexColumn.key_ordinal = 3
                        AND columnObject.name = N'Id')
               )
             """));
    }

    private static async Task AssertMySqlIndexShapeAsync(MySqlConnection connection)
    {
        Assert.AreEqual(3, await connection.ExecuteScalarAsync<int>(
            $"""
             SELECT COUNT(*)
             FROM INFORMATION_SCHEMA.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE()
               AND TABLE_NAME = 'fn_tenancy_domain_audit'
               AND INDEX_NAME = '{IndexName}'
               AND (
                    (SEQ_IN_INDEX = 1 AND COLUMN_NAME = 'TenantId')
                    OR (SEQ_IN_INDEX = 2 AND COLUMN_NAME = 'OccurredAtUtc')
                    OR (SEQ_IN_INDEX = 3 AND COLUMN_NAME = 'Id')
               )
             """));
    }

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
