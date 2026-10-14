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
            var rows = (await first.QueryAsync<Claimed>(Sql("ClaimPendingSqlServer"), new { BatchSize = 1, Now = DateTime.UtcNow }, transaction)).AsList();
            Assert.AreEqual(id, rows.Single().Id);
        }
        Assert.HasCount(0, await ClaimAsync(provider, second));
        await transaction.RollbackAsync();
        CollectionAssert.AreEqual(new[] { id }, (await ClaimAsync(provider, second)).ToArray());
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
            return (await connection.QueryAsync<Claimed>(Sql("ClaimPendingSqlServer"), new { BatchSize = 1, Now = DateTime.UtcNow }, commandTimeout: 10)).Select(row => row.Id).ToArray();
        await using var transaction = await connection.BeginTransactionAsync();
        var ids = (await connection.QueryAsync<Guid>(Sql("SelectClaimableIdsMySql"), new { BatchSize = 1 }, transaction, commandTimeout: 10)).AsList();
        if (ids.Count == 0) { await transaction.CommitAsync(); return []; }
        Assert.AreEqual(ids.Count, await connection.ExecuteAsync(Sql("ClaimByIdsMySql"), new { Ids = ids, Now = DateTime.UtcNow }, transaction));
        var rows = (await connection.QueryAsync<Claimed>(Sql("SelectByIds"), new { Ids = ids }, transaction)).AsList();
        Assert.AreEqual(ids.Count, rows.Count); Assert.IsTrue(rows.All(row => row.StatusKey == "processing"));
        await transaction.CommitAsync(); return rows.Select(row => row.Id).ToArray();
    }

    private static Task<int> CancelAsync(DbConnection connection, Guid id, Guid owner, long version) =>
        connection.ExecuteAsync(Sql("Cancel"), new { Id = id, RequestedByUserId = owner, ExpectedVersion = version, CompletedAtUtc = DateTime.UtcNow });
    private static Task<string> StatusAsync(DbConnection connection, Guid id) => connection.QuerySingleAsync<string>(
        "SELECT StatusKey FROM fn_document_preview_task WHERE Id=@Id", new { Id = id });
    private static DbConnection Connection(DatabaseProvider provider, string cs) => provider == DatabaseProvider.SqlServer ? new SqlConnection(cs)
        : new MySqlConnection(MySqlConnectionStringPolicy.Create(cs, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
    private static string Sql(string field) => ((SqlStatement)typeof(DocumentModule).Assembly
        .GetType("Full.NET.Modules.Document.Persistence.DocumentPreviewTaskSql", true)!.GetField(field)!.GetValue(null)!).Text;
    private sealed class Claimed { public Guid Id { get; set; } public string StatusKey { get; set; } = ""; }
}
