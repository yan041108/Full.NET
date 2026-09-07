using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Reporting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Reporting;

/// <summary>使用生产领取 SQL 验证报表导出任务的租户隔离、并发争抢与到期租约重领。</summary>
internal static class ReportingExportTaskClaimPersistenceAssertions
{
    /// <summary>领取必须带可信 TenantId；租户 A 不能拿走租户 B 的排队导出。</summary>
    /// <param name="provider">正式支持的数据库提供程序。</param>
    public static async Task Claim_is_isolated_to_current_tenant_async(DatabaseProvider provider)
    {
        var (first, second, tenantA, tenantB, taskA, taskB) = await SeedQueuedPairAsync(provider).ConfigureAwait(false);
        await using var connection = first;
        await using var unused = second;
        var now = DateTime.UtcNow;
        var claimed = await ClaimAsync(provider, connection, tenantA, now).ConfigureAwait(false);
        Assert.AreEqual(taskA, claimed);
        Assert.AreEqual("queued", await StatusAsync(connection, taskB).ConfigureAwait(false));
        Assert.AreEqual("processing", await StatusAsync(connection, taskA).ConfigureAwait(false));
        var stolen = await ClaimAsync(provider, connection, tenantA, now.AddSeconds(1)).ConfigureAwait(false);
        Assert.IsNull(stolen);
        _ = tenantB;
    }

    /// <summary>两个实例同时领取同一租户的一条排队导出时，只能有一个写入租约。</summary>
    /// <param name="provider">正式支持的数据库提供程序。</param>
    public static async Task Concurrent_claim_admits_only_one_owner_async(DatabaseProvider provider)
    {
        var (first, second, tenantA, _, taskA, _) = await SeedQueuedPairAsync(provider).ConfigureAwait(false);
        await using var firstConnection = first;
        await using var secondConnection = second;
        var now = DateTime.UtcNow;
        var claims = await Task.WhenAll(
            ClaimAsync(provider, firstConnection, tenantA, now),
            ClaimAsync(provider, secondConnection, tenantA, now)).ConfigureAwait(false);
        Assert.AreEqual(1, claims.Count(id => id == taskA));
        Assert.AreEqual(1, claims.Count(id => id is null));
        Assert.AreEqual("processing", await StatusAsync(firstConnection, taskA).ConfigureAwait(false));
    }

    /// <summary>处理中但租约已到期的导出必须能被另一连接重新领取。</summary>
    /// <param name="provider">正式支持的数据库提供程序。</param>
    public static async Task Expired_processing_lease_can_be_reclaimed_async(DatabaseProvider provider)
    {
        var (first, second, tenantA, _, taskA, _) = await SeedQueuedPairAsync(provider).ConfigureAwait(false);
        await using var connection = first;
        await using var unused = second;
        var expired = DateTime.UtcNow.AddMinutes(-2);
        await connection.ExecuteAsync(
            """
            UPDATE fn_reporting_export_task
            SET StatusKey = 'processing', LeaseId = @LeaseId, LeaseExpiresAtUtc = @LeaseExpiresAtUtc
            WHERE Id = @Id
            """,
            new { Id = taskA, LeaseId = Guid.CreateVersion7(), LeaseExpiresAtUtc = expired }).ConfigureAwait(false);
        var claimed = await ClaimAsync(provider, connection, tenantA, DateTime.UtcNow).ConfigureAwait(false);
        Assert.AreEqual(taskA, claimed);
        Assert.AreEqual("processing", await StatusAsync(connection, taskA).ConfigureAwait(false));
    }

    /// <summary>写入两个租户各一条 queued 导出任务，返回两条连接。</summary>
    private static async Task<(DbConnection First, DbConnection Second, Guid TenantA, Guid TenantB, Guid TaskA, Guid TaskB)> SeedQueuedPairAsync(DatabaseProvider provider)
    {
        var connectionString = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync().ConfigureAwait(false)
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync().ConfigureAwait(false);
        var runner = new DbUpMigrationRunner(Options.Create(new DatabaseOptions
        {
            Provider = provider,
            ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 300,
        }), NullLoggerFactory.Instance, MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions());
        await runner.MigrateAsync().ConfigureAwait(false);
        var first = Connection(provider, connectionString);
        var second = Connection(provider, connectionString);
        await first.OpenAsync().ConfigureAwait(false);
        await second.OpenAsync().ConfigureAwait(false);
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        var taskA = Guid.CreateVersion7();
        var taskB = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        await InsertQueuedAsync(first, taskA, tenantA, now).ConfigureAwait(false);
        await InsertQueuedAsync(first, taskB, tenantB, now.AddMilliseconds(1)).ConfigureAwait(false);
        return (first, second, tenantA, tenantB, taskA, taskB);
    }

    /// <summary>按生产 Insert 语句写入一条可领取的导出任务。</summary>
    private static Task<int> InsertQueuedAsync(DbConnection connection, Guid id, Guid tenantId, DateTime createdAtUtc) =>
        connection.ExecuteAsync(Sql("Insert"), new
        {
            Id = id,
            TenantId = tenantId,
            DefinitionId = Guid.CreateVersion7(),
            VersionNumber = 1,
            DefinitionKey = "sales",
            DefinitionName = "sales",
            FormatKey = "excel",
            ParametersJson = "{}",
            StatusKey = "queued",
            OutputFileId = (Guid?)null,
            OutputFileName = (string?)null,
            RowCount = 0,
            ErrorCode = (string?)null,
            ErrorMessage = (string?)null,
            RequestedByUserId = Guid.CreateVersion7(),
            CreatedAtUtc = createdAtUtc,
            CompletedAtUtc = (DateTime?)null,
            LeaseId = (Guid?)null,
            LeaseExpiresAtUtc = (DateTime?)null,
            ActorPermissionCodesJson = "[]",
            Version = 1L,
        });

    /// <summary>按提供程序执行生产领取语句，返回被占用的任务标识。</summary>
    private static async Task<Guid?> ClaimAsync(DatabaseProvider provider, DbConnection connection, Guid tenantId, DateTime now)
    {
        var leaseId = Guid.CreateVersion7();
        var leaseExpiresAtUtc = now.AddSeconds(60);
        if (provider == DatabaseProvider.SqlServer)
        {
            var rows = await connection.QueryAsync<ClaimedRow>(
                Sql("ClaimQueuedSqlServer"),
                new { TenantId = tenantId, Now = now, LeaseId = leaseId, LeaseExpiresAtUtc = leaseExpiresAtUtc }).ConfigureAwait(false);
            var list = rows.AsList();
            return list.Count == 0 ? null : list[0].Id;
        }

        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        var ids = (await connection.QueryAsync<Guid>(
            Sql("SelectClaimableIdsMySql"),
            new { TenantId = tenantId, Now = now },
            transaction).ConfigureAwait(false)).AsList();
        if (ids.Count == 0)
        {
            await transaction.RollbackAsync().ConfigureAwait(false);
            return null;
        }

        await connection.ExecuteAsync(
            Sql("ClaimByIdsMySql"),
            new { Ids = ids, TenantId = tenantId, Now = now, LeaseId = leaseId, LeaseExpiresAtUtc = leaseExpiresAtUtc },
            transaction).ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return ids[0];
    }

    /// <summary>读取导出任务当前状态。</summary>
    private static Task<string> StatusAsync(DbConnection connection, Guid id) =>
        connection.QuerySingleAsync<string>("SELECT StatusKey FROM fn_reporting_export_task WHERE Id=@Id", new { Id = id });

    /// <summary>只投影 OUTPUT 的主键，避免把领取行的全部列绑到测试模型。</summary>
    private sealed class ClaimedRow
    {
        public Guid Id { get; set; }
    }

    /// <summary>读取实际生产 SQL，避免复制一份实现使集成测试失去约束作用。</summary>
    private static string Sql(string field) => ((SqlStatement)typeof(ReportingModule).Assembly
        .GetType("Full.NET.Modules.Reporting.Persistence.ReportingExportTaskSql", true)!
        .GetField(field)!.GetValue(null)!).Text;

    /// <summary>创建标准 UUID 字节序的独立测试连接。</summary>
    private static DbConnection Connection(DatabaseProvider provider, string connectionString) => provider == DatabaseProvider.SqlServer
        ? new SqlConnection(connectionString)
        : new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
}
