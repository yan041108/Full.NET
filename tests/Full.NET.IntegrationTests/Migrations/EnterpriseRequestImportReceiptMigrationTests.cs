using System.Data.Common;
using System.Reflection;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>真实双库验证回执唯一键、事务回滚与重跑迁移；内存替身无法证明这些持久化语义。</summary>
[TestClass]
public sealed class EnterpriseRequestImportReceiptMigrationTests
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
        SampleMigrationRecoverySupport.Run(provider, cs, "003_EnterpriseRequestImportRows.sql");
        await using DbConnection connection = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(cs) : ReviewFixMigrationRecoverySupport.MySqlConnection(cs);
        await connection.OpenAsync();
        var tenant = Guid.CreateVersion7();
        var task = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();
        var position = Guid.CreateVersion7();
        var args = new { Id = id, TenantId = tenant, TaskId = task, LineNumber = 1,
            Digest = new string('A', 64), RequestId = position, CreatedAtUtc = DateTime.UtcNow };
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await connection.ExecuteAsync(Sql("InsertReceipt"), args, transaction);
            await transaction.RollbackAsync();
        }
        Assert.IsNull(await connection.QuerySingleOrDefaultAsync<Guid?>(Sql("FindRequest"), args));
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await connection.ExecuteAsync(Sql("InsertReceipt"), args, transaction);
            await transaction.CommitAsync();
        }
        var found = await connection.QuerySingleAsync<Guid>(Sql("FindRequest"), args);
        Assert.AreEqual(position, found);
        Assert.IsNull(await connection.QuerySingleOrDefaultAsync<Guid?>(Sql("FindRequest"),
            new { TenantId = Guid.CreateVersion7(), TaskId = task, LineNumber = 1 }));
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, "003_EnterpriseRequestImportRows.sql",
            provider == DatabaseProvider.SqlServer);
        Assert.AreEqual(1, SampleMigrationRecoverySupport.Run(provider, cs, "003_EnterpriseRequestImportRows.sql"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM demo_enterprise_request_import_row"));
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
        SampleMigrationRecoverySupport.Run(provider, cs, "003_EnterpriseRequestImportRows.sql");
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
                await connection.ExecuteAsync(Sql("InsertReceipt"), new { Id = id, TenantId = tenant, TaskId = task,
                    LineNumber = 1, Digest = new string('A', 64), RequestId = Guid.CreateVersion7(), CreatedAtUtc = DateTime.UtcNow }, transaction);
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
        var type = typeof(EnterpriseRequestModule).Assembly.GetType("Full.NET.Modules.EnterpriseRequest.Features.ImportExport.EnterpriseRequestImportWriter", true)!;
        return ((SqlStatement)type.GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Text;
    }
}
