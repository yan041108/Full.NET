using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Tests.Shared.CodeGeneration;
using Full.NET.Data.MySql;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.CodeGeneration;

/// <summary>直接执行显式生成草案，验证未记账DDL的恢复；不声明应用Migrator注册已完成。</summary>
[TestClass]
public sealed class GeneratedMigrationDraftRecoveryTests
{
    [TestMethod]
    public async Task SqlServer_generated_tenant_index_recovers_after_table_creation_and_preserves_rows()
    {
        await using var connection = new SqlConnection(await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await AssertRecoveryAsync(connection, sqlServer: true);
    }

    [TestMethod]
    public async Task MySql_generated_atomic_table_and_index_repeat_preserves_rows()
    {
        await using var connection = new MySqlConnection(MySqlConnectionStringPolicy.Create(
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), MySqlGuidStorageMode.Binary16,
            allowUserVariables: false));
        await AssertRecoveryAsync(connection, sqlServer: false);
    }

    private static async Task AssertRecoveryAsync(DbConnection connection, bool sqlServer)
    {
        var schema = GeneratedMigrationRecoveryFixture.CreateSchema();
        var provider = sqlServer ? "SqlServer" : "MySql";
        var sql = CrudArtifactGenerator.Generate(schema).Single(artifact =>
            artifact.RelativePath == $"templates/migrations/{provider}/CreateProduct.sql.template").Content;
        await connection.OpenAsync();
        if (sqlServer)
        {
            // 冻结到建表END模拟首步提交后进程中断，不执行索引，也没有DbUp记账。
            var tableEnd = sql.IndexOf("END;", StringComparison.Ordinal);
            Assert.IsTrue(tableEnd >= 0);
            await connection.ExecuteAsync(sql[..(tableEnd + "END;".Length)]);
            Assert.AreEqual(0, (await ReadIndexColumnsAsync(connection, sqlServer)).Length);
        }
        else
        {
            // MySQL草案把索引放在单条CREATE TABLE中，不能伪造该语句内部的半提交状态。
            await connection.ExecuteAsync(sql);
            CollectionAssert.AreEqual(new[] { "TenantId", "Id" }, await ReadIndexColumnsAsync(connection, sqlServer));
        }

        var id = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        await connection.ExecuteAsync(
            "INSERT INTO acme_catalog_product (Id, TenantId, Name, IsActive) VALUES (@Id, @TenantId, @Name, @IsActive)",
            new { Id = id, TenantId = tenantId, Name = "preserved-recovery-row", IsActive = true });
        await connection.ExecuteAsync(sql);
        await connection.ExecuteAsync(sql);
        CollectionAssert.AreEqual(new[] { "TenantId", "Id" }, await ReadIndexColumnsAsync(connection, sqlServer));
        Assert.AreEqual("preserved-recovery-row", await connection.QuerySingleAsync<string>(
            "SELECT Name FROM acme_catalog_product WHERE Id = @Id AND TenantId = @TenantId",
            new { Id = id, TenantId = tenantId }));
        Assert.IsTrue(await connection.QuerySingleAsync<bool>(
            "SELECT IsActive FROM acme_catalog_product WHERE Id = @Id AND TenantId = @TenantId",
            new { Id = id, TenantId = tenantId }));
        var columnCount = await connection.ExecuteScalarAsync<int>(sqlServer
            ? "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'acme_catalog_product'"
            : "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'acme_catalog_product'");
        Assert.AreEqual(4, columnCount);
    }

    private static async Task<string[]> ReadIndexColumnsAsync(DbConnection connection, bool sqlServer) =>
        (await connection.QueryAsync<string>(sqlServer
            ? """
              SELECT c.name FROM sys.indexes i
              JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
              JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE i.object_id = OBJECT_ID(N'dbo.acme_catalog_product', N'U')
                AND i.name = N'IX_acme_catalog_product_TenantId_Id' AND i.type = 1 AND i.is_disabled = 0
                AND ic.key_ordinal > 0
              ORDER BY ic.key_ordinal
              """
            : """
              SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.STATISTICS
              WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'acme_catalog_product'
                AND INDEX_NAME = 'IX_acme_catalog_product_TenantId_Id'
              ORDER BY SEQ_IN_INDEX
              """)).ToArray();
}
