using System.Data.Common;
using System.Reflection;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Organization;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>真实双库验证回执唯一键、事务回滚与重跑迁移；内存替身无法证明这些持久化语义。</summary>
[TestClass]
public sealed class Migration209PositionImportReceiptTests
{
    [TestMethod]
    public Task SqlServer_receipt_atomicity_and_migration_reentry() => Receipt_is_atomic_tenant_isolated_and_migration_reentrant(DatabaseProvider.SqlServer);
    [TestMethod]
    public Task MySql_receipt_atomicity_and_migration_reentry() => Receipt_is_atomic_tenant_isolated_and_migration_reentrant(DatabaseProvider.MySql);

    private static async Task Receipt_is_atomic_tenant_isolated_and_migration_reentrant(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = ReviewFixMigrationRecoverySupport.CreateRunner(provider, cs);
        await runner.MigrateAsync();
        await using DbConnection connection = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(cs) : ReviewFixMigrationRecoverySupport.MySqlConnection(cs);
        await connection.OpenAsync();
        var tenant = Guid.CreateVersion7();
        var task = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();
        var position = Guid.CreateVersion7();
        var args = new { Id = id, TenantId = tenant, TaskId = task, LineNumber = 1,
            PayloadHash = new string('A', 64), CreatedAtUtc = DateTime.UtcNow };
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await connection.ExecuteAsync(Sql("Insert"), args, transaction);
            await connection.ExecuteAsync(Sql("Complete"), new { Id = id, TenantId = tenant, PositionId = position }, transaction);
            await transaction.RollbackAsync();
        }
        Assert.IsNull(await connection.QuerySingleOrDefaultAsync(Sql("Find"), args));
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await connection.ExecuteAsync(Sql("Insert"), args, transaction);
            await connection.ExecuteAsync(Sql("Complete"), new { Id = id, TenantId = tenant, PositionId = position }, transaction);
            await transaction.CommitAsync();
        }
        var found = await connection.QuerySingleAsync(Sql("Find"), args);
        Assert.AreEqual(position, (Guid)found.PositionId);
        Assert.IsNull(await connection.QuerySingleOrDefaultAsync(Sql("Find"),
            new { TenantId = Guid.CreateVersion7(), TaskId = task, LineNumber = 1 }));
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, "209_OrganizationPositionImportReceipt.sql",
            provider == DatabaseProvider.SqlServer);
        Assert.AreEqual(1, (await runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_organization_position_import"));
    }

    [TestMethod]
    public Task SqlServer_concurrent_receipt_claim() => Concurrent_receipt_claim_has_one_winner(DatabaseProvider.SqlServer);
    [TestMethod]
    public Task MySql_concurrent_receipt_claim() => Concurrent_receipt_claim_has_one_winner(DatabaseProvider.MySql);

    private static async Task Concurrent_receipt_claim_has_one_winner(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await ReviewFixMigrationRecoverySupport.CreateRunner(provider, cs).MigrateAsync();
        var tenant = Guid.CreateVersion7();
        var task = Guid.CreateVersion7();
        var claims = await Task.WhenAll(ClaimAsync(), ClaimAsync());
        Assert.AreEqual(1, claims.Count(won => won));

        async Task<bool> ClaimAsync()
        {
            await using DbConnection connection = provider == DatabaseProvider.SqlServer
                ? new SqlConnection(cs) : ReviewFixMigrationRecoverySupport.MySqlConnection(cs);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            var id = Guid.CreateVersion7();
            try
            {
                await connection.ExecuteAsync(Sql("Insert"), new { Id = id, TenantId = tenant, TaskId = task,
                    LineNumber = 1, PayloadHash = new string('A', 64), CreatedAtUtc = DateTime.UtcNow }, transaction);
                await connection.ExecuteAsync(Sql("Complete"), new { Id = id, TenantId = tenant,
                    PositionId = Guid.CreateVersion7() }, transaction);
                await transaction.CommitAsync();
                return true;
            }
            catch (SqlException exception) when (exception.Number is 2601 or 2627)
            {
                await transaction.RollbackAsync();
                return false;
            }
            catch (MySqlConnector.MySqlException exception) when (exception.Number == 1062)
            {
                await transaction.RollbackAsync();
                return false;
            }
        }
    }

    // 读取生产 SQL，避免在持久化测试中复制一套可能漂移的查询。
    private static string Sql(string field)
    {
        var type = typeof(OrganizationModule).Assembly.GetType("Full.NET.Modules.Organization.Persistence.PositionImportSql", true)!;
        return ((SqlStatement)type.GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Text;
    }
}
