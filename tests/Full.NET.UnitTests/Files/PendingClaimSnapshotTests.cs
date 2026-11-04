using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;
using Full.NET.Modules.Files.Reconciliation;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

/// <summary>对账释放只能作用于仍符合扫描快照的 Pending Claim。</summary>
[TestClass]
public sealed class PendingClaimSnapshotTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "confirmed")]
    [DataRow(DatabaseProvider.MySql, "confirmed")]
    [DataRow(DatabaseProvider.SqlServer, "refreshed")]
    [DataRow(DatabaseProvider.MySql, "refreshed")]
    [DataRow(DatabaseProvider.SqlServer, "unchanged")]
    [DataRow(DatabaseProvider.MySql, "unchanged")]
    [DataRow(DatabaseProvider.SqlServer, "unknown")]
    [DataRow(DatabaseProvider.MySql, "unknown")]
    public async Task Release_uses_scanned_pending_snapshot(DatabaseProvider provider, string change)
    {
        var now = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        var record = new HostFileReferenceClaimRecord(Guid.CreateVersion7(), "claim-snapshot", Guid.CreateVersion7(),
            HostFileReferenceClaimConsumerModules.Document, Guid.CreateVersion7(), HostFileReferenceClaimStates.Pending,
            null, 1, now.AddHours(-2), now.AddHours(-1), null, null);
        var state = record.State;
        var updatedAt = record.UpdatedAtUtc;
        var queries = Substitute.For<IQueryExecutor>();
        queries.QueryAsync<HostFileReferenceClaimRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<HostFileReferenceClaimRecord>>([record]));
        var probe = Substitute.For<IHostFileReferenceClaimProbe>();
        probe.ConsumerModule.Returns(record.ConsumerModule);
        probe.ProbeReferenceAsync(record.ConsumerReferenceId, record.FileId, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            // 探测与释放之间另一调用完成确认或刷新，旧的“不存在”结论不得覆盖新状态。
            if (change == "confirmed") state = HostFileReferenceClaimStates.Active;
            if (change is "confirmed" or "refreshed") updatedAt = now;
            return Task.FromResult(new HostFileReferenceClaimProbeResult(change == "unknown"
                ? (HostFileReferenceClaimProbeOutcome)99 : HostFileReferenceClaimProbeOutcome.NotFound));
        });
        SqlStatement? releasedStatement = null;
        Dictionary<string, object?>? releasedParameters = null;
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            releasedStatement = call.ArgAt<SqlStatement>(0);
            releasedParameters = (Dictionary<string, object?>)call.ArgAt<object>(1);
            var matches = state == HostFileReferenceClaimStates.Pending
                || (state == HostFileReferenceClaimStates.Active && releasedStatement.Text.Contains("OR State = @ActiveState", StringComparison.Ordinal));
            if (releasedParameters.TryGetValue("ObservedUpdatedAtUtc", out var observed)) matches &= observed is DateTimeOffset value && value == updatedAt;
            if (matches) state = HostFileReferenceClaimStates.Released;
            return Task.FromResult(matches ? 1 : 0);
        });
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var runner = new PendingHostFileReferenceClaimReconciliationRunner(queries, commands, [probe],
            Options.Create(new DatabaseOptions { Provider = provider }), clock);
        var result = await runner.RunOnceAsync(new PendingHostFileReferenceClaimReconciliationOptions(), CancellationToken.None);

        Assert.AreEqual(change == "unchanged" ? 1 : 0, result.Released);
        if (change == "unknown")
        {
            Assert.AreEqual(1, result.ProbeFailures);
            Assert.AreEqual(HostFileReferenceClaimStates.Pending, state);
            Assert.IsNull(releasedStatement);
            return;
        }
        Assert.AreEqual(change == "confirmed" ? HostFileReferenceClaimStates.Active
            : change == "refreshed" ? HostFileReferenceClaimStates.Pending : HostFileReferenceClaimStates.Released, state);
        Assert.IsNotNull(releasedStatement);
        Assert.IsFalse(releasedStatement.Text.Contains("OR State = @ActiveState", StringComparison.Ordinal));
        StringAssert.Contains(releasedStatement.Text, "Id = @Id");
        StringAssert.Contains(releasedStatement.Text, "UpdatedAtUtc = @ObservedUpdatedAtUtc");
        Assert.AreEqual(SqlDataScope.HostOnly, releasedStatement.Scope);
        Assert.AreEqual(record.Id, releasedParameters!["Id"]);
        Assert.AreEqual(record.UpdatedAtUtc, releasedParameters["ObservedUpdatedAtUtc"]);
    }
}
