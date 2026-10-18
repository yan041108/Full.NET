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
    /// <summary>同库复用两种完成路径，真实行锁等待到期后不能覆盖可重领任务。</summary>
    private static async Task CompletionLockWaitAsync(DatabaseProvider provider, DbConnection connection, DbConnection other,
        Guid tenantA, Guid tenantB, Guid taskA)
    {
        var lockField = provider == DatabaseProvider.SqlServer ? "LockCompletionSqlServer" : "LockCompletionMySql";
        Assert.IsNull(await other.QuerySingleOrDefaultAsync<CompletionRow>(Sql(lockField), new { TenantId = tenantB, Id = taskA }));
        foreach (var failed in new[] { false, true })
        {
            var rowCount = provider == DatabaseProvider.SqlServer ? "[RowCount]" : "RowCount";
            await connection.ExecuteAsync($"UPDATE fn_reporting_export_task SET StatusKey='queued',LeaseId=NULL,LeaseExpiresAtUtc=NULL,OutputFileId=NULL,CompletedAtUtc=NULL,ErrorCode=NULL,ErrorMessage=NULL,{rowCount}=0 WHERE TenantId=@TenantId AND Id=@Id",
                new { TenantId = tenantA, Id = taskA });
            Assert.AreEqual(taskA, await ClaimAsync(provider, connection, tenantA, DateTime.UtcNow));
            var lease = await connection.QuerySingleAsync<Guid>("SELECT LeaseId FROM fn_reporting_export_task WHERE Id=@Id", new { Id = taskA });
            await using var held = await connection.BeginTransactionAsync();
            var expiry = DateTime.UtcNow.AddSeconds(1);
            await connection.ExecuteAsync("UPDATE fn_reporting_export_task SET LeaseExpiresAtUtc=@Expiry WHERE TenantId=@TenantId AND Id=@Id",
                new { TenantId = tenantA, Id = taskA, Expiry = expiry }, held);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var completion = CompleteAfterLockAsync(provider, other, tenantA, taskA, lease, failed, started);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.IsFalse(completion.IsCompleted);
            var wait = expiry - DateTime.UtcNow + TimeSpan.FromMilliseconds(100);
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
            await held.CommitAsync();
            Assert.AreEqual(0, await completion.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.AreEqual("processing", await StatusAsync(connection, taskA));
            Assert.AreEqual(lease, await connection.QuerySingleAsync<Guid>("SELECT LeaseId FROM fn_reporting_export_task WHERE Id=@Id", new { Id = taskA }));
            Assert.IsNull(await connection.QuerySingleAsync<Guid?>("SELECT OutputFileId FROM fn_reporting_export_task WHERE Id=@Id", new { Id = taskA }));
            Assert.AreEqual(taskA, await ClaimAsync(provider, other, tenantA, DateTime.UtcNow));
            Assert.AreNotEqual(lease, await connection.QuerySingleAsync<Guid>("SELECT LeaseId FROM fn_reporting_export_task WHERE Id=@Id", new { Id = taskA }));
        }
    }

    private static async Task<int> CompleteAfterLockAsync(DatabaseProvider provider, DbConnection connection, Guid tenant, Guid task,
        Guid lease, bool failed, TaskCompletionSource started)
    {
        await using var transaction = await connection.BeginTransactionAsync();
        var pending = connection.QuerySingleAsync<CompletionRow>(Sql(provider == DatabaseProvider.SqlServer ? "LockCompletionSqlServer" : "LockCompletionMySql"),
            new { TenantId = tenant, Id = task }, transaction, commandTimeout: 10);
        started.TrySetResult(); var row = await pending; var now = DateTime.UtcNow;
        var expiry = row.LeaseExpiresAtUtc switch { DateTimeOffset value => value.UtcDateTime,
            DateTime value => DateTime.SpecifyKind(value, DateTimeKind.Utc), _ => throw new InvalidDataException("导出租约时间类型不符。") };
        var field = failed ? "CompleteFailed" : "CompleteSucceeded";
        if (provider == DatabaseProvider.SqlServer) field += "SqlServer";
        var affected = row.TenantId == tenant && row.StatusKey == "processing" && row.LeaseId == lease && expiry > now
            ? await connection.ExecuteAsync(Sql(field), new { TenantId = tenant, Id = task, LeaseId = lease, Now = now,
                CompletedAtUtc = now, StatusKey = failed ? "failed" : "succeeded", OutputFileId = Guid.CreateVersion7(),
                OutputFileName = "probe.xlsx", RowCount = 1, ErrorCode = "probe", ErrorMessage = "probe" }, transaction) : 0;
        await transaction.CommitAsync(); return affected;
    }

    private sealed class CompletionRow
    {
        public Guid TenantId { get; set; }
        public Guid? LeaseId { get; set; }
        public object? LeaseExpiresAtUtc { get; set; }
        public string StatusKey { get; set; } = "";
    }

    /// <summary>生产取消 SQL 拒绝跨租户、非创建人与旧版本，取消后不可重新领取。</summary>
    public static async Task Cancellation_is_scoped_and_fenced_async(DatabaseProvider provider)
    {
        var (first, second, tenantA, tenantB, taskA, taskB) = await SeedQueuedPairAsync(provider).ConfigureAwait(false);
        await using var connection = first;
        await using var other = second;
        var owner = await connection.QuerySingleAsync<Guid>("SELECT RequestedByUserId FROM fn_reporting_export_task WHERE Id=@Id", new { Id = taskA });
        var now = DateTime.UtcNow;
        Assert.AreEqual(0, await CancelAsync(connection, tenantB, taskA, owner, 1, now));
        Assert.AreEqual(0, await CancelAsync(connection, tenantA, taskA, Guid.CreateVersion7(), 1, now));
        Assert.AreEqual(0, await CancelAsync(connection, tenantA, taskA, owner, 0, now));
        Assert.AreEqual(1, await CancelAsync(connection, tenantA, taskA, owner, 1, now));
        Assert.AreEqual("cancelled", await StatusAsync(connection, taskA));
        Assert.AreEqual("queued", await StatusAsync(connection, taskB));
        Assert.IsNull(await ClaimAsync(provider, other, tenantA, now.AddSeconds(1)));
        Assert.AreEqual(0, await CancelAsync(connection, tenantA, taskA, owner, 1, now));
    }

    /// <summary>两连接竞争领取和取消，同一排队主行只能有一个状态转换获胜。</summary>
    public static async Task Cancellation_and_claim_have_one_winner_async(DatabaseProvider provider)
    {
        var (first, second, tenantA, _, taskA, _) = await SeedQueuedPairAsync(provider).ConfigureAwait(false);
        await using var connection = first;
        await using var worker = second;
        var owner = await connection.QuerySingleAsync<Guid>("SELECT RequestedByUserId FROM fn_reporting_export_task WHERE Id=@Id", new { Id = taskA });
        var now = DateTime.UtcNow;
        var cancel = CancelAsync(connection, tenantA, taskA, owner, 1, now);
        var claim = ClaimAsync(provider, worker, tenantA, now);
        await Task.WhenAll(cancel, claim);
        Assert.AreEqual(1, cancel.Result + (claim.Result == taskA ? 1 : 0));
        Assert.AreEqual(cancel.Result == 1 ? "cancelled" : "processing", await StatusAsync(connection, taskA));
    }

    /// <summary>已经领取的租约不得被取消请求清空或改变终态。</summary>
    public static async Task Cancellation_preserves_processing_lease_async(DatabaseProvider provider)
    {
        var (first, second, tenantA, _, taskA, _) = await SeedQueuedPairAsync(provider).ConfigureAwait(false);
        await using var connection = first;
        await using var worker = second;
        var now = DateTime.UtcNow;
        Assert.AreEqual(taskA, await ClaimAsync(provider, worker, tenantA, now));
        var owner = await connection.QuerySingleAsync<Guid>("SELECT RequestedByUserId FROM fn_reporting_export_task WHERE Id=@Id", new { Id = taskA });
        var lease = await connection.QuerySingleAsync<Guid>("SELECT LeaseId FROM fn_reporting_export_task WHERE Id=@Id", new { Id = taskA });
        Assert.AreEqual(0, await CancelAsync(connection, tenantA, taskA, owner, 2, now));
        Assert.AreEqual("processing", await StatusAsync(connection, taskA));
        Assert.AreEqual(lease, await connection.QuerySingleAsync<Guid>("SELECT LeaseId FROM fn_reporting_export_task WHERE Id=@Id", new { Id = taskA }));
    }

    private static Task<int> CancelAsync(DbConnection connection, Guid tenantId, Guid taskId, Guid owner, long version, DateTime now) =>
        connection.ExecuteAsync(Sql("Cancel"), new { TenantId = tenantId, Id = taskId, RequestedByUserId = owner, ExpectedVersion = version, CompletedAtUtc = now });

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
        var (first, second, tenantA, tenantB, taskA, _) = await SeedQueuedPairAsync(provider).ConfigureAwait(false);
        await using var connection = first;
        await using var unused = second;
        // 整秒在双库和参数绑定中可精确表示，避免舍入破坏相等边界。
        var instant = DateTime.UtcNow;
        var now = new DateTime(instant.Ticks - instant.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        var oldLeaseId = Guid.CreateVersion7();
        var currentLeaseId = Guid.Empty;
        await connection.ExecuteAsync(
            """
            UPDATE fn_reporting_export_task
            SET StatusKey = 'processing', LeaseId = @LeaseId, LeaseExpiresAtUtc = @LeaseExpiresAtUtc
            WHERE Id = @Id
            """,
            new { Id = taskA, LeaseId = oldLeaseId, LeaseExpiresAtUtc = now.AddMinutes(-2) }).ConfigureAwait(false);
        await AssertWritesAsync(oldLeaseId, tenantA, 0).ConfigureAwait(false);
        foreach (var expiry in new DateTime?[] { now, null })
        {
            await connection.ExecuteAsync("UPDATE fn_reporting_export_task SET LeaseExpiresAtUtc=@Expiry WHERE Id=@Id",
                new { Id = taskA, Expiry = expiry }).ConfigureAwait(false);
            await AssertWritesAsync(oldLeaseId, tenantA, 0).ConfigureAwait(false);
        }
        var claimed = await ClaimAsync(provider, connection, tenantA, now).ConfigureAwait(false);
        Assert.AreEqual(taskA, claimed);
        Assert.AreEqual("processing", await StatusAsync(connection, taskA).ConfigureAwait(false));
        currentLeaseId = await connection.QuerySingleAsync<Guid>("SELECT LeaseId FROM fn_reporting_export_task WHERE Id=@Id", new { Id = taskA }).ConfigureAwait(false);
        await AssertWritesAsync(oldLeaseId, tenantA, 0).ConfigureAwait(false);
        await AssertWritesAsync(currentLeaseId, tenantB, 0).ConfigureAwait(false);
        await AssertWritesAsync(currentLeaseId, tenantA, 1).ConfigureAwait(false);
        await CompletionLockWaitAsync(provider, connection, unused, tenantA, tenantB, taskA).ConfigureAwait(false);

        // 同一数据库覆盖成功与失败完成语句，避免为每个边界重复迁移。
        async Task AssertWritesAsync(Guid leaseId, Guid tenantId, int expected)
        {
            foreach (var statement in new[] { "CompleteSucceeded", "CompleteFailed" })
            {
                var field = provider == DatabaseProvider.SqlServer ? statement + "SqlServer" : statement;
                var parameters = new
                {
                    Id = taskA, TenantId = tenantId, LeaseId = leaseId, Now = now,
                    StatusKey = statement == "CompleteSucceeded" ? "succeeded" : "failed",
                    OutputFileId = Guid.CreateVersion7(), OutputFileName = "probe.xlsx", RowCount = 1,
                    CompletedAtUtc = now, ErrorCode = "probe", ErrorMessage = "probe",
                };
                Assert.AreEqual(expected, await connection.ExecuteAsync(Sql(field), parameters).ConfigureAwait(false), $"{provider}/{statement}");
                if (expected == 0)
                {
                    Assert.AreEqual("processing", await StatusAsync(connection, taskA).ConfigureAwait(false));
                    Assert.AreEqual(0, await connection.QuerySingleAsync<int>(
                        provider == DatabaseProvider.SqlServer ? "SELECT [RowCount] FROM fn_reporting_export_task WHERE Id=@Id"
                            : "SELECT RowCount FROM fn_reporting_export_task WHERE Id=@Id", new { Id = taskA }).ConfigureAwait(false));
                }
                else
                {
                    Assert.AreEqual(0, await connection.ExecuteAsync(Sql(field), parameters).ConfigureAwait(false));
                    await connection.ExecuteAsync("UPDATE fn_reporting_export_task SET StatusKey='processing', LeaseId=@LeaseId, LeaseExpiresAtUtc=@Expiry WHERE Id=@Id",
                        new { Id = taskA, LeaseId = currentLeaseId, Expiry = now.AddMinutes(1) }).ConfigureAwait(false);
                }
            }
        }
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
        await InsertQueuedAsync(provider, first, taskA, tenantA, now).ConfigureAwait(false);
        await InsertQueuedAsync(provider, first, taskB, tenantB, now.AddMilliseconds(1)).ConfigureAwait(false);
        return (first, second, tenantA, tenantB, taskA, taskB);
    }

    /// <summary>按生产 Insert 语句写入一条可领取的导出任务。</summary>
    private static Task<int> InsertQueuedAsync(
        DatabaseProvider provider,
        DbConnection connection,
        Guid id,
        Guid tenantId,
        DateTime createdAtUtc) =>
        connection.ExecuteAsync(Sql(provider == DatabaseProvider.SqlServer ? "InsertSqlServer" : "Insert"), new
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
