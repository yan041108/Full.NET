using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Features.HostFileReferences;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;
using Full.NET.Modules.Files.Reconciliation;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

/// <summary>历史不可复用引用的 Active 对账必须保守释放，不能依赖已丢失的删除事件。</summary>
[TestClass]
public sealed class ActiveClaimReconciliationTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, false)]
    [DataRow(DatabaseProvider.MySql, false)]
    [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.MySql, true)]
    public async Task Stale_active_claim_is_released_only_when_document_reference_is_missing(DatabaseProvider provider, bool versionExists)
    {
        var now = new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);
        var record = new HostFileReferenceClaimRecord(Guid.CreateVersion7(), "historical-active", Guid.CreateVersion7(),
            HostFileReferenceClaimConsumerModules.Document, Guid.CreateVersion7(), HostFileReferenceClaimStates.Active,
            null, 1, now.AddHours(-2), now.AddHours(-1), now.AddHours(-1), null);
        var queries = new TestQueries(record, versionExists);
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
        var probe = new HostDocumentVersionReferenceProbe(queries, clock);
        var commands = Substitute.For<ICommandExecutor>();
        SqlStatement? release = null;
        Dictionary<string, object?>? parameters = null;
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            release = call.ArgAt<SqlStatement>(0);
            parameters = (Dictionary<string, object?>)call.ArgAt<object>(1);
            return Task.FromResult(1);
        });
        var runner = new ActiveHostFileReferenceClaimReconciliationRunner(queries, commands, [probe],
            Options.Create(new DatabaseOptions { Provider = provider }), clock);
        var result = await runner.RunOnceAsync(new() { MaxBatchesPerRun = 1 }, CancellationToken.None);
        Assert.AreEqual(1, result.Scanned);
        Assert.AreEqual(0, result.ProbeFailures);
        Assert.AreEqual(versionExists ? 0 : 1, result.Released);
        if (versionExists)
        {
            Assert.IsNull(release);
            return;
        }
        Assert.IsNotNull(release); Assert.IsNotNull(parameters);
        StringAssert.Contains(release.Text, "State = @ActiveState");
        StringAssert.Contains(release.Text, "UpdatedAtUtc = @ObservedUpdatedAtUtc");
        StringAssert.Contains(release.Text, "FileId = @ObservedFileId");
        StringAssert.Contains(release.Text, "ConsumerReferenceId = @ObservedConsumerReferenceId");
        Assert.AreEqual(record.UpdatedAtUtc, parameters["ObservedUpdatedAtUtc"]);
        Assert.AreEqual(SqlDataScope.HostOnly, release.Scope);
    }

    private sealed class TestQueries(HostFileReferenceClaimRecord record, bool versionExists) : IQueryExecutor
    {
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<T> rows = typeof(T) == typeof(HostFileReferenceClaimRecord) ? [(T)(object)record]
                : statement == DocumentItemSql.FindVersionsForClaimProbe && versionExists
                    ? [(T)(object)new DocumentVersionRecord { Id = record.ConsumerReferenceId, FileId = record.FileId }]
                    : [];
            return Task.FromResult(rows);
        }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(default(T));
    }
}
