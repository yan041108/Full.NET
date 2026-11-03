using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>清理的入口和提交边界必须维持取消、可信范围和有界工作集；真实回滚由双库用例验证。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionCleanupTests
{
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Global_scan_matches_its_exact_catalog_and_never_selects_correction_bodies(bool mysql)
    {
        var sql = mysql ? AiKnowledgeCleanupWorkerSql.ScanMySql : AiKnowledgeCleanupWorkerSql.ScanSqlServer;
        var directory = new DirectoryInfo(Environment.GetEnvironmentVariable("FULLNET_TEST_REPOSITORY_ROOT") ?? AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx"))) directory = directory.Parent;
        Assert.IsNotNull(directory);
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "contracts", "architecture", "global-sql-statements.json")));
        var entry = catalog.RootElement.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("statementName").GetString() == sql.Name);
        Assert.AreEqual(SqlDataScope.Global, sql.Scope);
        foreach (var fragment in entry.GetProperty("requiredSqlFragments").EnumerateArray()) StringAssert.Contains(sql.Text, fragment.GetString()!);
        Assert.IsFalse(sql.Text.Contains("OriginalPayload", StringComparison.Ordinal)); Assert.IsFalse(sql.Text.Contains("InitialPayload", StringComparison.Ordinal));
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Cancellation_or_ambient_transaction_prevents_all_io(bool ambient)
    {
        var f = new Fixture(); using var stop = new CancellationTokenSource();
        if (ambient) f.State.HasTransaction.Returns(true); else stop.Cancel();
        if (ambient) await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Run(stop.Token));
        else await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Run(stop.Token));
        Assert.IsFalse(f.Query.ReceivedCalls().Any()); Assert.IsFalse(f.Command.ReceivedCalls().Any()); Assert.AreEqual(0, f.Completed);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Oversized_or_duplicate_revision_set_cannot_start_body_erasure(bool duplicate)
    {
        var f = new Fixture(); var id = Guid.CreateVersion7();
        f.Query.QueryAsync<Guid>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>(duplicate ? [id, id] : Enumerable.Range(0, 26).Select(_ => Guid.CreateVersion7()).ToArray()));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.Run(default));
        Assert.AreEqual(1, f.Completed); Assert.AreEqual(1, f.Command.ReceivedCalls().Count());
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Cancellation_or_scope_change_after_write_is_seen_before_transaction_can_commit(bool scope)
    {
        var f = new Fixture(); using var stop = new CancellationTokenSource();
        f.Command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (scope) f.Tenant.SetTenant(new(Guid.CreateVersion7(), "other", "其他范围")); else stop.Cancel();
            return Task.FromResult(1);
        });
        if (scope) await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Run(stop.Token));
        else await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Run(stop.Token));
        Assert.AreEqual(0, f.Completed); Assert.AreEqual(1, f.Command.ReceivedCalls().Count());
    }

    private sealed class Fixture
    {
        internal readonly IQueryExecutor Query = Substitute.For<IQueryExecutor>();
        internal readonly ICommandExecutor Command = Substitute.For<ICommandExecutor>();
        internal readonly IDataTransactionState State = Substitute.For<IDataTransactionState>();
        internal readonly CurrentTenantAccessor Tenant = new();
        internal int Completed;
        private readonly Guid knowledge = Guid.CreateVersion7(), document = Guid.CreateVersion7(), owner = Guid.CreateVersion7(), source = Guid.CreateVersion7();
        private readonly AiKnowledgeCorrectionCleanupStore store;
        internal Fixture()
        {
            Tenant.SetHost();
            var transaction = Substitute.For<ICommandTransaction>();
            transaction.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>()).Returns(async call =>
            { var result = await call.ArgAt<Func<CancellationToken, Task<bool>>>(0)(call.ArgAt<CancellationToken>(1)); Completed++; return result; });
            Query.QuerySingleOrDefaultAsync<Guid>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(call =>
                Task.FromResult(call.ArgAt<SqlStatement>(0).Name.StartsWith("ai.knowledge_publication.lock_", StringComparison.Ordinal) ? document : source));
            Query.QueryAsync<Guid>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Guid>>([]));
            Command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            store = new(Query, Command, transaction, State, Tenant, clock, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
        }
        internal Task Run(CancellationToken token) => store.CleanupSourceAsync(knowledge, document, owner, source, token);
    }
}
