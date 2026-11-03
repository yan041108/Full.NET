using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>读取校对头的明确死锁只能使只读快照失效，不重试写入或隐藏其他数据库故障。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionReadConcurrencyTests
{
    [TestMethod]
    [DataRow(false, 1)] [DataRow(true, 1)] [DataRow(false, 2)] [DataRow(true, 2)]
    public async Task Deadlock_at_either_head_read_returns_no_snapshot_without_writes(bool mysql, int phase)
    {
        var fixture = new Fixture(mysql, phase, new DataCommandException(DataCommandFailureKind.Deadlock, new IOException()));
        Assert.IsNull(await fixture.Read(default));
        Assert.AreEqual(phase, fixture.HeadReads);
        fixture.AssertReadOnly();
    }

    [TestMethod]
    [DataRow(DataCommandFailureKind.UniqueConstraint)] [DataRow(DataCommandFailureKind.InvalidValue)]
    public async Task Other_classified_database_errors_remain_visible(DataCommandFailureKind kind)
    {
        var error = new DataCommandException(kind, new IOException()); var fixture = new Fixture(false, 1, error);
        Assert.AreSame(error, await Assert.ThrowsExactlyAsync<DataCommandException>(() => fixture.Read(default)));
        fixture.AssertReadOnly();
    }

    [TestMethod]
    public async Task Unknown_read_failure_is_not_reported_as_concurrency()
    {
        var error = new IOException("受控未知读取错误。"); var fixture = new Fixture(false, 1, error);
        Assert.AreSame(error, await Assert.ThrowsExactlyAsync<IOException>(() => fixture.Read(default)));
        fixture.AssertReadOnly();
    }

    [TestMethod]
    public async Task Cancellation_during_deadlock_remains_cancellation()
    {
        using var canceled = new CancellationTokenSource();
        var fixture = new Fixture(false, 1, new DataCommandException(DataCommandFailureKind.Deadlock, new IOException()), canceled.Cancel);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Read(canceled.Token));
        fixture.AssertReadOnly();
    }

    [TestMethod]
    public async Task Ambient_transaction_is_rejected_before_querying()
    {
        var fixture = new Fixture(false, 1, new DataCommandException(DataCommandFailureKind.Deadlock, new IOException()));
        fixture.State.HasTransaction.Returns(true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Read(default));
        Assert.AreEqual(0, fixture.HeadReads); fixture.AssertReadOnly();
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Stable_snapshot_still_requires_both_matching_heads(bool mysql)
    {
        var fixture = new Fixture(mysql, 0, null);
        Assert.IsNotNull(await fixture.Read(default));
        Assert.AreEqual(2, fixture.HeadReads); fixture.AssertReadOnly();
    }

    private sealed class Fixture
    {
        internal readonly IDataTransactionState State = Substitute.For<IDataTransactionState>();
        private readonly ICommandExecutor command = Substitute.For<ICommandExecutor>();
        private readonly ICommandTransaction transaction = Substitute.For<ICommandTransaction>();
        private readonly AiKnowledgeCorrectionStore store;
        private readonly AiKnowledgeCorrectionStore.Binding binding;
        private readonly AiKnowledgeParsedDocument parsed;
        internal int HeadReads;

        internal Fixture(bool mysql, int failedRead, Exception? error, Action? beforeFailure = null)
        {
            var query = Substitute.For<IQueryExecutor>(); var tenant = new CurrentTenantAccessor(); tenant.SetHost();
            var bytes = Encoding.UTF8.GetBytes("原始校对内容"); var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            parsed = AiKnowledgeParsedDocument.FromText(new("原始校对内容", hash, bytes.Length), AiKnowledgeTextFormat.PlainText, default)!;
            var document = AiKnowledgeCorrectionDocument.Create(parsed, Guid.CreateVersion7, default);
            const string runtime = "unit-runtime-v1"; var initial = AiKnowledgeCorrectionWireCodec.Encode(document, runtime, default);
            var source = new AiKnowledgeCorrectionSourceRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), hash,
                parsed.ContentHash, runtime, AiKnowledgeCorrectionWireCodec.SourceHash(parsed, runtime, default),
                AiKnowledgeParsedWireCodec.Encode(parsed, default), initial, Convert.ToHexStringLower(SHA256.HashData(initial)), Guid.CreateVersion7(), DateTimeOffset.UtcNow);
            binding = new(Guid.CreateVersion7(), Guid.CreateVersion7(), source.VersionId, source.ActorUserId, 2);
            query.QuerySingleOrDefaultAsync<AiKnowledgeCorrectionHeadRecord>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (++HeadReads == failedRead) { beforeFailure?.Invoke(); return Task.FromException<AiKnowledgeCorrectionHeadRecord?>(error!); }
                return Task.FromResult<AiKnowledgeCorrectionHeadRecord?>(new(source.VersionId, source.Id, null, 0));
            });
            query.QuerySingleOrDefaultAsync<AiKnowledgeCorrectionSourceRecord>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<AiKnowledgeCorrectionSourceRecord?>(source));
            store = new(query, command, transaction, State, tenant, Substitute.For<IClock>(), Substitute.For<IIdGenerator>(),
                Options.Create(new DatabaseOptions { Provider = mysql ? DatabaseProvider.MySql : DatabaseProvider.SqlServer }));
        }

        internal Task<AiKnowledgeCorrectionSnapshot?> Read(CancellationToken token) =>
            store.ReadCurrentAsync(binding, parsed.Format, parsed.ContentHash, parsed.SizeBytes, parsed.Policy, token);

        internal void AssertReadOnly()
        {
            Assert.IsFalse(command.ReceivedCalls().Any());
            Assert.IsFalse(transaction.ReceivedCalls().Any(), "只读处理不能进入业务事务，更不能隐藏提交后的失败。");
        }
    }
}
