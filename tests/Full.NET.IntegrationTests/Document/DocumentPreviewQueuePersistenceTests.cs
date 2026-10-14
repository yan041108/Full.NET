using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Document;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Document;

/// <summary>真实双连接执行生产 SQL，覆盖领取所有权、取消竞争与锁定候选排除。</summary>
[TestClass]
public sealed class DocumentPreviewQueuePersistenceTests
{
    [TestMethod]
    public Task MySql_queue_transitions_preserve_single_owner() => VerifyAsync(DatabaseProvider.MySql);
    [TestMethod]
    public Task SqlServer_queue_transitions_preserve_single_owner() => VerifyAsync(DatabaseProvider.SqlServer);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var runner = new DbUpMigrationRunner(Options.Create(new DatabaseOptions { Provider = provider, ConnectionString = cs,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16, CommandTimeoutSeconds = 300 }), NullLoggerFactory.Instance,
            MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions());
        await runner.MigrateAsync();
        await using var first = Connection(provider, cs); await using var second = Connection(provider, cs);
        await first.OpenAsync(); await second.OpenAsync();
        var owner = Guid.CreateVersion7(); var item = Guid.CreateVersion7();
        await first.ExecuteAsync("""
            INSERT INTO fn_document_item (Id,TenantId,Title,CreatedAtUtc,CreatedByUserId,Version)
            VALUES (@Id,NULL,'Preview queue',@Now,@Owner,1)
            """, new { Id = item, Now = DateTime.UtcNow, Owner = owner });

        // 两个实例同读同一排队行时只能有一个真实领取结果。
        var id = await SeedAsync(first, item, owner);
        var claims = await Task.WhenAll(ClaimAsync(provider, first), ClaimAsync(provider, second));
        Assert.AreEqual(1, claims.Sum(rows => rows.Count)); Assert.AreEqual(id, claims.SelectMany(rows => rows).Single());

        // 创建人、旧版本及处理状态均不得被取消覆盖。
        Assert.AreEqual(0, await CancelAsync(first, id, owner, 2));
        Assert.AreEqual("processing", await StatusAsync(first, id));
        id = await SeedAsync(first, item, owner);
        Assert.AreEqual(0, await CancelAsync(first, id, Guid.CreateVersion7(), 1));
        Assert.AreEqual(0, await CancelAsync(first, id, owner, 0));
        Assert.AreEqual(1, await CancelAsync(first, id, owner, 1));
        Assert.AreEqual("cancelled", await StatusAsync(first, id));
        Assert.HasCount(0, await ClaimAsync(provider, second));

        // 取消和领取竞争同一主行，任何先后顺序都只能有一次状态转换。
        id = await SeedAsync(first, item, owner);
        var cancel = CancelAsync(first, id, owner, 1); var claim = ClaimAsync(provider, second);
        await Task.WhenAll(cancel, claim);
        Assert.AreEqual(1, cancel.Result + claim.Result.Count);
        Assert.AreEqual(cancel.Result == 1 ? "cancelled" : "processing", await StatusAsync(first, id));

        // 持有候选/领取锁的实例未提交时，另一实例必须跳过，不能等待后盲读其结果。
        id = await SeedAsync(first, item, owner);
        await using var transaction = await first.BeginTransactionAsync();
        if (provider == DatabaseProvider.MySql)
        {
            var ids = (await first.QueryAsync<Guid>(Sql("SelectClaimableIdsMySql"), new { BatchSize = 1 }, transaction)).AsList();
            CollectionAssert.AreEqual(new[] { id }, ids.ToArray());
        }
        else
        {
            var rows = (await first.QueryAsync<Claimed>(Sql("ClaimPendingSqlServer"), ClaimParameters(), transaction)).AsList();
            Assert.AreEqual(id, rows.Single().Id);
        }
        Assert.HasCount(0, await ClaimAsync(provider, second));
        await transaction.RollbackAsync();
        CollectionAssert.AreEqual(new[] { id }, (await ClaimAsync(provider, second)).ToArray());

        // 领取身份及未过期租约同时仲裁完成，失效收口之后旧 Worker 不得覆盖终态。
        id = await SeedAsync(first, item, owner);
        CollectionAssert.AreEqual(new[] { id }, (await ClaimAsync(provider, first)).ToArray());
        var leaseId = await first.QuerySingleAsync<Guid>("SELECT LeaseId FROM fn_document_preview_task WHERE Id=@Id", new { Id = id });
        Assert.AreEqual(0, await CompleteAsync(first, id, Guid.CreateVersion7(), false));
        Assert.AreEqual(0, await CompleteAsync(first, id, Guid.CreateVersion7(), true));
        Assert.AreEqual(1, await CompleteAsync(first, id, leaseId, false));
        Assert.AreEqual("succeeded", await StatusAsync(first, id));

        id = await SeedAsync(first, item, owner);
        await ClaimAsync(provider, first);
        leaseId = await first.QuerySingleAsync<Guid>("SELECT LeaseId FROM fn_document_preview_task WHERE Id=@Id", new { Id = id });
        await first.ExecuteAsync("UPDATE fn_document_preview_task SET LeaseExpiresAtUtc=@Expired WHERE Id=@Id", new { Id = id, Expired = DateTime.UtcNow.AddSeconds(-1) });
        Assert.AreEqual(0, await CompleteAsync(first, id, leaseId, false));
        Assert.AreEqual(0, await CompleteAsync(first, id, leaseId, true));
        Assert.AreEqual(1, await ExpireAsync(provider, first));
        Assert.AreEqual(0, await ExpireAsync(provider, first));

        // 完成等待锁跨越真实到期时，必须在锁返回后取时间，不能沿用语句发起时的时间。
        foreach (var failed in new[] { false, true })
        {
            id = await SeedAsync(first, item, owner);
            await ClaimAsync(provider, first);
            leaseId = await first.QuerySingleAsync<Guid>("SELECT LeaseId FROM fn_document_preview_task WHERE Id=@Id", new { Id = id });
            await using var held = await first.BeginTransactionAsync();
            var expires = DateTime.UtcNow.AddSeconds(1);
            await first.ExecuteAsync("UPDATE fn_document_preview_task SET LeaseExpiresAtUtc=@Expires WHERE Id=@Id", new { Id = id, Expires = expires }, held);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var completion = CompleteUnderLockAsync(provider, second, id, leaseId, failed, started);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(completion.IsCompleted);
            var wait = expires - DateTime.UtcNow + TimeSpan.FromMilliseconds(100);
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
            await held.CommitAsync();
            Assert.AreEqual(0, await completion.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.AreEqual("processing", await StatusAsync(first, id));
            Assert.AreEqual(1, await ExpireAsync(provider, first));
            Assert.AreEqual("document.office_preview.lease_expired", await first.QuerySingleAsync<string>("SELECT ErrorCode FROM fn_document_preview_task WHERE Id=@Id", new { Id = id }));
        }
        Assert.AreEqual("failed", await StatusAsync(first, id));
        Assert.AreEqual("document.office_preview.lease_expired", await first.QuerySingleAsync<string>("SELECT ErrorCode FROM fn_document_preview_task WHERE Id=@Id", new { Id = id }));
        Assert.AreEqual(0, await CompleteAsync(first, id, leaseId, false));

        // 旧版无租约 processing 行按开始时间恢复；缺开始时间时用创建时间，不改活跃任务。
        var legacy = await SeedAsync(first, item, owner); var missingStart = await SeedAsync(first, item, owner);
        var recent = await SeedAsync(first, item, owner);
        await first.ExecuteAsync("UPDATE fn_document_preview_task SET StatusKey='processing',StartedAtUtc=@Old WHERE Id=@Id", new { Id = legacy, Old = DateTime.UtcNow.AddHours(-1) });
        await first.ExecuteAsync("UPDATE fn_document_preview_task SET StatusKey='processing',CreatedAtUtc=@Old WHERE Id=@Id", new { Id = missingStart, Old = DateTime.UtcNow.AddHours(-1) });
        await first.ExecuteAsync("UPDATE fn_document_preview_task SET StatusKey='processing',StartedAtUtc=@Now WHERE Id=@Id", new { Id = recent, Now = DateTime.UtcNow });
        Assert.AreEqual(1, await ExpireAsync(provider, first, 1));
        Assert.AreEqual(1, await ExpireAsync(provider, first, 1));
        Assert.AreEqual("failed", await StatusAsync(first, legacy)); Assert.AreEqual("failed", await StatusAsync(first, missingStart));
        Assert.AreEqual("processing", await StatusAsync(first, recent));
        Assert.AreEqual(0, await ExpireAsync(provider, first));
    }

    private static async Task<Guid> SeedAsync(DbConnection connection, Guid item, Guid owner)
    {
        var id = Guid.CreateVersion7();
        await connection.ExecuteAsync(Sql("Insert"), new { Id = id, DocumentItemId = item, VersionId = (Guid?)null,
            DocumentTitle = "Preview queue", SourceFileId = Guid.CreateVersion7(), SourceFileName = "source.docx",
            SourceMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document", OutputFileId = (Guid?)null,
            StatusKey = "pending", ProviderKey = "disabled", ErrorCode = (string?)null, RequestedByUserId = owner,
            CreatedAtUtc = DateTime.UtcNow, StartedAtUtc = (DateTime?)null, CompletedAtUtc = (DateTime?)null, Version = 1L });
        return id;
    }

    private static async Task<IReadOnlyList<Guid>> ClaimAsync(DatabaseProvider provider, DbConnection connection)
    {
        if (provider == DatabaseProvider.SqlServer)
            return (await connection.QueryAsync<Claimed>(Sql("ClaimPendingSqlServer"), ClaimParameters(), commandTimeout: 10)).Select(row => row.Id).ToArray();
        await using var transaction = await connection.BeginTransactionAsync();
        var ids = (await connection.QueryAsync<Guid>(Sql("SelectClaimableIdsMySql"), new { BatchSize = 1 }, transaction, commandTimeout: 10)).AsList();
        if (ids.Count == 0) { await transaction.CommitAsync(); return []; }
        var parameters = ClaimParameters();
        parameters["Ids"] = ids;
        Assert.AreEqual(ids.Count, await connection.ExecuteAsync(Sql("ClaimByIdsMySql"), parameters, transaction));
        var rows = (await connection.QueryAsync<Claimed>(Sql("SelectByIds"), new { Ids = ids }, transaction)).AsList();
        Assert.AreEqual(ids.Count, rows.Count); Assert.IsTrue(rows.All(row => row.StatusKey == "processing"));
        await transaction.CommitAsync(); return rows.Select(row => row.Id).ToArray();
    }

    private static Task<int> CancelAsync(DbConnection connection, Guid id, Guid owner, long version) =>
        connection.ExecuteAsync(Sql("Cancel"), new { Id = id, RequestedByUserId = owner, ExpectedVersion = version, CompletedAtUtc = DateTime.UtcNow });
    private static Dictionary<string, object?> ClaimParameters() => new()
    {
        ["BatchSize"] = 1, ["Now"] = DateTime.UtcNow, ["LeaseId"] = Guid.CreateVersion7(), ["LeaseExpiresAtUtc"] = DateTime.UtcNow.AddMinutes(30),
    };
    private static Task<int> CompleteAsync(DbConnection connection, Guid id, Guid leaseId, bool failed) => connection.ExecuteAsync(
        Sql(failed ? "MarkFailed" : "MarkSucceeded"), new { Id = id, LeaseId = leaseId, Now = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow, OutputFileId = Guid.CreateVersion7(), ErrorCode = "fixture-failure" });
    private static Task<int> ExpireAsync(DatabaseProvider provider, DbConnection connection, int batchSize = 5) => connection.ExecuteAsync(
        Sql(provider == DatabaseProvider.SqlServer ? "ExpireProcessingSqlServer" : "ExpireProcessingMySql"), new { BatchSize = batchSize,
            Now = DateTime.UtcNow, LegacyCutoff = DateTime.UtcNow.AddMinutes(-5), ErrorCode = "document.office_preview.lease_expired" });

    private static async Task<int> CompleteUnderLockAsync(DatabaseProvider provider, DbConnection connection, Guid id, Guid leaseId,
        bool failed, TaskCompletionSource started)
    {
        await using var transaction = await connection.BeginTransactionAsync();
        var pending = connection.QuerySingleAsync<CompletionFence>(Sql(provider == DatabaseProvider.SqlServer ? "LockCompletionSqlServer" : "LockCompletionMySql"),
            new { Id = id }, transaction, commandTimeout: 10);
        started.TrySetResult();
        var row = await pending;
        var now = DateTime.UtcNow;
        var expires = row.LeaseExpiresAtUtc switch
        {
            DateTimeOffset value => value.UtcDateTime,
            DateTime value => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => throw new InvalidDataException("租约时间类型与正式提供程序不符。"),
        };
        var affected = row.LeaseId == leaseId && row.StatusKey == "processing" && expires > now
            ? await connection.ExecuteAsync(Sql(failed ? "MarkFailed" : "MarkSucceeded"), new { Id = id, LeaseId = leaseId, Now = now,
                CompletedAtUtc = now, OutputFileId = Guid.CreateVersion7(), ErrorCode = "fixture-failure" }, transaction) : 0;
        await transaction.CommitAsync();
        return affected;
    }
    private static Task<string> StatusAsync(DbConnection connection, Guid id) => connection.QuerySingleAsync<string>(
        "SELECT StatusKey FROM fn_document_preview_task WHERE Id=@Id", new { Id = id });
    private static DbConnection Connection(DatabaseProvider provider, string cs) => provider == DatabaseProvider.SqlServer ? new SqlConnection(cs)
        : new MySqlConnection(MySqlConnectionStringPolicy.Create(cs, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
    private static string Sql(string field) => ((SqlStatement)typeof(DocumentModule).Assembly
        .GetType("Full.NET.Modules.Document.Persistence.DocumentPreviewTaskSql", true)!.GetField(field)!.GetValue(null)!).Text;
    private sealed class Claimed { public Guid Id { get; set; } public string StatusKey { get; set; } = ""; }
    private sealed class CompletionFence
    {
        public Guid? LeaseId { get; set; }
        public object? LeaseExpiresAtUtc { get; set; }
        public string StatusKey { get; set; } = "";
    }
}
