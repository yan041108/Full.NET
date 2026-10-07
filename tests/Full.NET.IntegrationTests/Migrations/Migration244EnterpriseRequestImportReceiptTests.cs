using System.Data.Common;
using System.Reflection;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>真实双库验证回执唯一键、事务回滚与重跑迁移；内存替身无法证明这些持久化语义。</summary>
[TestClass]
public sealed class Migration244EnterpriseRequestImportReceiptTests
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
        using var scope = new Through244Scope(provider, cs);
        var runner = scope.Runner;
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
            await connection.ExecuteAsync(Sql("Complete"), new { Id = id, TenantId = tenant, EntityId = position }, transaction);
            await transaction.RollbackAsync();
        }
        Assert.IsNull(await connection.QuerySingleOrDefaultAsync(Sql("Find"), args));
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await connection.ExecuteAsync(Sql("Insert"), args, transaction);
            await connection.ExecuteAsync(Sql("Complete"), new { Id = id, TenantId = tenant, EntityId = position }, transaction);
            await transaction.CommitAsync();
        }
        var found = await connection.QuerySingleAsync(Sql("Find"), args);
        Assert.AreEqual(position, (Guid)found.EntityId);
        Assert.IsNull(await connection.QuerySingleOrDefaultAsync(Sql("Find"),
            new { TenantId = Guid.CreateVersion7(), TaskId = task, LineNumber = 1 }));
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, "244_DemoEnterpriseRequestImportReceipt.sql",
            provider == DatabaseProvider.SqlServer);
        Assert.AreEqual(1, (await runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM demo_enterprise_request_import_receipt"));
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
        using var scope = new Through244Scope(provider, cs);
        await scope.Runner.MigrateAsync();
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
                    EntityId = Guid.CreateVersion7() }, transaction);
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

    // 清单冻结目标脚本，后续迁移不能漂入本恢复测试。
    private sealed class Through244Scope : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("fullnet-receipt244-").FullName;
        internal Full.NET.Migrations.DbUp.DbUpMigrationRunner Runner { get; }
        internal Through244Scope(DatabaseProvider provider, string connectionString)
        {
            var segment = provider == DatabaseProvider.SqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.";
            var names = typeof(Full.NET.Migrations.DbUp.DbUpMigrationRunner).Assembly.GetManifestResourceNames()
                .Where(name => name.Contains(segment, StringComparison.Ordinal)
                    && NamingExpandTestMigrationRunner.IsThroughMigration(name, 244)).ToArray();
            Assert.IsTrue(names.Any(name => name.EndsWith("244_DemoEnterpriseRequestImportReceipt.sql", StringComparison.Ordinal)));
            var scripts = names.Select(name => new { name = name[(name.IndexOf(segment, StringComparison.Ordinal) + segment.Length)..] });
            File.WriteAllText(Path.Combine(directory, "framework-manifest.json"), System.Text.Json.JsonSerializer.Serialize(new
                { migrationInventory = new { selectionStatus = "preset-recovery-through244", scripts } }));
            Runner = new(Microsoft.Extensions.Options.Options.Create(new DatabaseOptions
                { Provider = provider, ConnectionString = connectionString, MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16, CommandTimeoutSeconds = 300 }),
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
                MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions(),
                Microsoft.Extensions.Options.Options.Create(new Full.NET.Migrations.DbUp.FrameworkManifestMigrationOptions { ContentRoot = directory }));
        }
        public void Dispose() => Directory.Delete(directory, recursive: true);
    }

    // 读取生产 SQL，避免在持久化测试中复制一套可能漂移的查询。
    private static string Sql(string field)
    {
        var type = typeof(EnterpriseRequestModule).Assembly.GetType("Full.NET.Modules.EnterpriseRequest.Persistence.EnterpriseRequestImportSql", true)!;
        return ((SqlStatement)type.GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Text;
    }
}
