using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证配额绑定列可恢复重跑，且不会猜测或覆盖历史归属。</summary>
[TestClass]
public sealed class Migration229QuotaMetricBindingRecoveryTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Migration_preserves_unbound_legacy_and_bound_rows_on_replay(bool sqlServer)
    {
        var connectionString = sqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = ReviewFixMigrationRecoverySupport.CreateRunner(
            sqlServer ? DatabaseProvider.SqlServer : DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync();
        await using DbConnection connection = sqlServer
            ? new SqlConnection(connectionString)
            : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        await connection.ExecuteAsync("ALTER TABLE fn_tenancy_quota_reservation DROP COLUMN MetricId");
        var id = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        await connection.ExecuteAsync("""
            INSERT INTO fn_tenancy_quota_reservation
                (Id, TenantId, MetricCode, OperationId, Amount, Status, ExpiresAtUtc, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES (@Id, @TenantId, 'identity.seats', 'legacy-binding', 1, 'Reserved', @Now, @Now, @Now, 1)
            """, new { Id = id, TenantId = Guid.CreateVersion7(), Now = now });
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, "229_TenancyQuotaMetricBinding.sql", sqlServer);
        Assert.AreEqual(1, (await runner.MigrateAsync()).ExecutedScriptCount);
        Assert.IsNull(await connection.ExecuteScalarAsync<Guid?>(
            "SELECT MetricId FROM fn_tenancy_quota_reservation WHERE Id = @Id", new { Id = id }));
        var metricId = Guid.CreateVersion7();
        await connection.ExecuteAsync("UPDATE fn_tenancy_quota_reservation SET MetricId = @MetricId WHERE Id = @Id",
            new { Id = id, MetricId = metricId });
        var unboundId = Guid.CreateVersion7();
        await connection.ExecuteAsync("""
            INSERT INTO fn_tenancy_quota_reservation
                (Id, TenantId, MetricCode, OperationId, Amount, Status, ExpiresAtUtc, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES (@Id, @TenantId, 'identity.seats', 'still-unbound', 1, 'Reserved', @Now, @Now, @Now, 1)
            """, new { Id = unboundId, TenantId = Guid.CreateVersion7(), Now = now });
        if (sqlServer)
        {
            // 模拟列已成功创建但注释步骤未完成，重跑需补齐说明。
            await connection.ExecuteAsync("""
                EXEC sys.sp_dropextendedproperty @name=N'MS_Description',
                    @level0type=N'SCHEMA', @level0name=N'dbo',
                    @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_reservation',
                    @level2type=N'COLUMN', @level2name=N'MetricId'
                """);
        }
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, "229_TenancyQuotaMetricBinding.sql", sqlServer);
        Assert.AreEqual(1, (await runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(metricId, await connection.ExecuteScalarAsync<Guid>(
            "SELECT MetricId FROM fn_tenancy_quota_reservation WHERE Id = @Id", new { Id = id }));
        Assert.IsNull(await connection.ExecuteScalarAsync<Guid?>(
            "SELECT MetricId FROM fn_tenancy_quota_reservation WHERE Id = @Id", new { Id = unboundId }));
        var comment = await connection.ExecuteScalarAsync<string>(sqlServer
            ? """
                SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties
                WHERE major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_reservation')
                    AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_reservation'), N'MetricId', 'ColumnId')
                    AND name = N'MS_Description'
                """
            : """
                SELECT COLUMN_COMMENT FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_tenancy_quota_reservation' AND COLUMN_NAME = 'MetricId'
                """);
        Assert.AreEqual("实际配额记录标识；NULL 表示历史归属待对账", comment);
        Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
    }
}
