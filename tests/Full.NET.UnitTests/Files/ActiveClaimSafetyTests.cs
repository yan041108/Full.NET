using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;
using Full.NET.Modules.Files.Reconciliation;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

/// <summary>不可复用引用的对账必须失败保留、按快照释放并跨轮公平推进。</summary>
[TestClass]
public sealed class ActiveClaimSafetyTests
{
    [TestMethod]
    [DataRow(0, 0, 0)]
    [DataRow(1, 1, 0)]
    [DataRow(2, 0, 1)]
    [DataRow(99, 0, 1)]
    [DataRow(-1, 0, 1)]
    public async Task Only_authoritative_not_found_releases(int outcome, int released, int failures)
    {
        var f = new Fixture();
        f.Probe.ProbeReferencesAsync(Arg.Any<IReadOnlyList<HostFileReferenceClaimProbeRequest>>(), Arg.Any<CancellationToken>())
            .Returns(call => outcome == -1 ? Task.FromException<IReadOnlyDictionary<Guid, HostFileReferenceClaimProbeResult>>(new IOException("probe failed"))
                : Task.FromResult(Answers(call.ArgAt<IReadOnlyList<HostFileReferenceClaimProbeRequest>>(0), (HostFileReferenceClaimProbeOutcome)outcome)));
        var result = await f.Runner().RunOnceAsync(new(), CancellationToken.None);
        Assert.AreEqual(released, result.Released); Assert.AreEqual(failures, result.ProbeFailures);
        Assert.AreEqual(released == 1 ? "released" : "active", f.Current.State);
    }

    [TestMethod]
    [DataRow("state")]
    [DataRow("time")]
    [DataRow("file")]
    [DataRow("module")]
    [DataRow("reference")]
    public async Task Changed_snapshot_is_preserved(string change)
    {
        var f = new Fixture();
        f.Probe.ProbeReferencesAsync(Arg.Any<IReadOnlyList<HostFileReferenceClaimProbeRequest>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            f.Current = change switch
            {
                "state" => f.Current with { State = "released" },
                "time" => f.Current with { UpdatedAtUtc = f.Now },
                "file" => f.Current with { FileId = Guid.CreateVersion7() },
                "module" => f.Current with { ConsumerModule = "identity" },
                _ => f.Current with { ConsumerReferenceId = Guid.CreateVersion7() },
            };
            return Task.FromResult(Answers(call.ArgAt<IReadOnlyList<HostFileReferenceClaimProbeRequest>>(0), HostFileReferenceClaimProbeOutcome.NotFound));
        });
        var result = await f.Runner().RunOnceAsync(new(), CancellationToken.None);
        Assert.AreEqual(0, result.Released);
        Assert.IsNull(f.Current.ReleasedAtUtc);
    }

    [TestMethod]
    public async Task Ordinary_probe_cannot_opt_in_to_active_release()
    {
        var f = new Fixture(); var ordinary = Substitute.For<IHostFileReferenceClaimProbe>(); ordinary.ConsumerModule.Returns("document");
        var result = await f.Runner([ordinary]).RunOnceAsync(new(), CancellationToken.None);
        Assert.AreEqual(0, result.Scanned); Assert.AreEqual(0, f.QueriesSeen.Count);
    }

    [TestMethod]
    public async Task Cancellation_after_probe_never_releases()
    {
        var f = new Fixture(); using var cancelled = new CancellationTokenSource();
        f.Probe.ProbeReferencesAsync(Arg.Any<IReadOnlyList<HostFileReferenceClaimProbeRequest>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            cancelled.Cancel(); return Task.FromResult(Answers(call.ArgAt<IReadOnlyList<HostFileReferenceClaimProbeRequest>>(0), HostFileReferenceClaimProbeOutcome.NotFound));
        });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Runner().RunOnceAsync(new(), cancelled.Token));
        Assert.AreEqual(0, f.Writes); Assert.AreEqual("active", f.Current.State);
    }

    [TestMethod]
    public async Task Shared_cursor_reaches_orphan_after_retained_first_page()
    {
        var f = new Fixture(); var orphan = f.Current with { Id = Guid.CreateVersion7(), ConsumerReferenceId = Guid.CreateVersion7(), UpdatedAtUtc = f.Current.UpdatedAtUtc.AddMinutes(1) };
        f.Rows.Add(orphan); var cursor = new ActiveHostFileReferenceClaimReconciliationCursor();
        f.Probe.ProbeReferencesAsync(Arg.Any<IReadOnlyList<HostFileReferenceClaimProbeRequest>>(), Arg.Any<CancellationToken>()).Returns(call =>
            Task.FromResult<IReadOnlyDictionary<Guid, HostFileReferenceClaimProbeResult>>(call.ArgAt<IReadOnlyList<HostFileReferenceClaimProbeRequest>>(0)
                .ToDictionary(r => r.ClaimId, r => new HostFileReferenceClaimProbeResult(r.ConsumerReferenceId == f.Current.ConsumerReferenceId
                    ? HostFileReferenceClaimProbeOutcome.Exists : HostFileReferenceClaimProbeOutcome.NotFound))));
        var options = new ActiveHostFileReferenceClaimReconciliationOptions { BatchSize = 1, MaxBatchesPerRun = 1 };
        Assert.AreEqual(0, (await f.Runner(cursor: cursor).RunOnceAsync(options, CancellationToken.None)).Released);
        Assert.AreEqual(1, (await f.Runner(cursor: cursor).RunOnceAsync(options, CancellationToken.None)).Released);
        Assert.AreEqual(orphan.Id, f.ReleasedIds.Single());
        Assert.IsTrue((int)f.QueriesSeen[1]["HasCursor"]! == 1);
    }

    [TestMethod]
    public async Task Scan_cycle_keeps_cutoff_until_end_then_resets()
    {
        var f = new Fixture(); var cursor = new ActiveHostFileReferenceClaimReconciliationCursor();
        f.Probe.ProbeReferencesAsync(Arg.Any<IReadOnlyList<HostFileReferenceClaimProbeRequest>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Answers(call.ArgAt<IReadOnlyList<HostFileReferenceClaimProbeRequest>>(0), HostFileReferenceClaimProbeOutcome.Exists)));
        var options = new ActiveHostFileReferenceClaimReconciliationOptions { BatchSize = 1, MaxBatchesPerRun = 1 };
        await f.Runner(cursor: cursor).RunOnceAsync(options, CancellationToken.None);
        var cutoff = f.QueriesSeen[0]["StaleBeforeUtc"];
        f.Clock.UtcNow.Returns(f.Now.AddHours(2));
        await f.Runner(cursor: cursor).RunOnceAsync(options, CancellationToken.None);
        Assert.AreEqual(cutoff, f.QueriesSeen[1]["StaleBeforeUtc"]);
        Assert.IsNull(cursor.Id); Assert.IsNull(cursor.StaleBeforeUtc);
    }

    [TestMethod]
    public async Task Disabled_reconciler_never_queries()
    {
        var f = new Fixture(); await f.Runner().RunOnceAsync(new() { Enabled = false }, CancellationToken.None);
        Assert.AreEqual(0, f.QueriesSeen.Count);
    }

    [TestMethod]
    [DataRow("BatchSize", 0)]
    [DataRow("BatchSize", 1001)]
    [DataRow("MaxBatchesPerRun", 0)]
    [DataRow("MinimumAgeSeconds", 0)]
    [DataRow("PollSeconds", 0)]
    public void Invalid_budgets_are_rejected(string field, int value)
    {
        var options = new ActiveHostFileReferenceClaimReconciliationOptions();
        typeof(ActiveHostFileReferenceClaimReconciliationOptions).GetProperty(field)!.SetValue(options, value);
        Assert.IsTrue(new ActiveHostFileReferenceClaimReconciliationOptionsValidator().Validate(null, options).Failed);
    }

    [TestMethod]
    public async Task Active_references_are_probed_once_per_owner_batch()
    {
        var f = new Fixture(); f.Rows.Add(f.Current with { Id = Guid.CreateVersion7(), ConsumerReferenceId = Guid.CreateVersion7() });
        var result = await f.Runner().RunOnceAsync(new(), CancellationToken.None);
        Assert.AreEqual(2, result.Released);
        Assert.AreEqual(0, f.Probe.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "ProbeReferenceAsync"));
        Assert.AreEqual(1, f.Probe.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "ProbeReferencesAsync"));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("null")]
    public async Task Missing_batch_answers_are_preserved(string mode)
    {
        var f = new Fixture();
        f.Probe.ProbeReferencesAsync(Arg.Any<IReadOnlyList<HostFileReferenceClaimProbeRequest>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, HostFileReferenceClaimProbeResult>>(mode == "null" ? null! : new Dictionary<Guid, HostFileReferenceClaimProbeResult>()));
        var result = await f.Runner().RunOnceAsync(new(), CancellationToken.None);
        Assert.AreEqual(1, result.ProbeFailures); Assert.AreEqual(0, result.Released); Assert.AreEqual(0, f.Writes);
    }

    private static IReadOnlyDictionary<Guid, HostFileReferenceClaimProbeResult> Answers(
        IReadOnlyList<HostFileReferenceClaimProbeRequest> requests, HostFileReferenceClaimProbeOutcome outcome) =>
        requests.ToDictionary(r => r.ClaimId, _ => new HostFileReferenceClaimProbeResult(outcome));

    private sealed class Fixture
    {
        internal readonly DateTimeOffset Now = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);
        internal readonly IQueryExecutor Query = Substitute.For<IQueryExecutor>();
        internal readonly ICommandExecutor Commands = Substitute.For<ICommandExecutor>();
        internal readonly IClock Clock = Substitute.For<IClock>();
        internal readonly IHostImmutableFileReferenceClaimProbe Probe = Substitute.For<IHostImmutableFileReferenceClaimProbe>();
        internal readonly List<HostFileReferenceClaimRecord> Rows = [];
        internal readonly List<Dictionary<string, object?>> QueriesSeen = [];
        internal readonly List<Guid> ReleasedIds = [];
        internal HostFileReferenceClaimRecord Current;
        internal int Writes;

        internal Fixture()
        {
            Clock.UtcNow.Returns(Now); Probe.ConsumerModule.Returns("document");
            Current = new(Guid.CreateVersion7(), "active-safety", Guid.CreateVersion7(), "document", Guid.CreateVersion7(), "active",
                null, 1, Now.AddHours(-2), Now.AddHours(-1), Now.AddHours(-1), null);
            Rows.Add(Current);
            Probe.ProbeReferencesAsync(Arg.Any<IReadOnlyList<HostFileReferenceClaimProbeRequest>>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(Answers(call.ArgAt<IReadOnlyList<HostFileReferenceClaimProbeRequest>>(0), HostFileReferenceClaimProbeOutcome.NotFound)));
            Query.QueryAsync<HostFileReferenceClaimRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var p = (Dictionary<string, object?>)call.ArgAt<object>(1); QueriesSeen.Add(p);
                var after = (Guid?)p["AfterId"]; var time = (DateTimeOffset)p["AfterUpdatedAtUtc"]!;
                return Task.FromResult<IReadOnlyList<HostFileReferenceClaimRecord>>(Rows.Where(r => !ReleasedIds.Contains(r.Id)
                    && r.UpdatedAtUtc <= (DateTimeOffset)p["StaleBeforeUtc"]!
                    && (after is null || r.UpdatedAtUtc > time || r.UpdatedAtUtc == time && r.Id.CompareTo(after.Value) > 0))
                    .OrderBy(r => r.UpdatedAtUtc).ThenBy(r => r.Id).Take((int)p["BatchSize"]!).ToArray());
            });
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Writes++; var p = (Dictionary<string, object?>)call.ArgAt<object>(1); var id = (Guid)p["Id"]!;
                var row = id == Current.Id ? Current : Rows.Single(r => r.Id == id);
                var matched = row.State == (string)p["ActiveState"]! && row.UpdatedAtUtc == (DateTimeOffset)p["ObservedUpdatedAtUtc"]!
                    && row.FileId == (Guid)p["ObservedFileId"]! && row.ConsumerModule == (string)p["ObservedConsumerModule"]!
                    && row.ConsumerReferenceId == (Guid)p["ObservedConsumerReferenceId"]!;
                if (matched) { ReleasedIds.Add(id); if (id == Current.Id) Current = row with { State = "released", ReleasedAtUtc = Now }; }
                return Task.FromResult(matched ? 1 : 0);
            });
        }

        internal ActiveHostFileReferenceClaimReconciliationRunner Runner(IEnumerable<IHostFileReferenceClaimProbe>? probes = null,
            ActiveHostFileReferenceClaimReconciliationCursor? cursor = null) => new(Query, Commands, probes ?? [Probe],
                Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }), Clock, cursor);
    }
}
