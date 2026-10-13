using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Reconciliation;
using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

/// <summary>三条待处理链路必须有界环扫，首批失败不阻塞后序恢复，保护上界不能随新增记录无限移动。</summary>
[TestClass]
public sealed class PendingReconciliationCycleTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "upload")]
    [DataRow(DatabaseProvider.MySql, "upload")]
    [DataRow(DatabaseProvider.SqlServer, "claim")]
    [DataRow(DatabaseProvider.MySql, "claim")]
    [DataRow(DatabaseProvider.SqlServer, "tenant")]
    [DataRow(DatabaseProvider.MySql, "tenant")]
    public async Task First_failed_page_does_not_starve_later_rows_and_is_retried(DatabaseProvider provider, string kind)
    {
        var f = new Fixture(provider, kind);
        Assert.AreEqual(0, (await f.RunAsync()).Written);
        Assert.AreEqual(1, (await f.RunAsync()).Written, "有限批次必须沿前次进度恢复后序记录。");
        Assert.AreEqual(0, (await f.RunAsync()).Scanned);
        Assert.AreEqual(1, (await f.RunAsync()).Scanned, "完整周期结束后重试首批失败。");
        Assert.HasCount(1, f.Rows); Assert.IsNull(f.Tenant.Id);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "upload")]
    [DataRow(DatabaseProvider.MySql, "upload")]
    [DataRow(DatabaseProvider.SqlServer, "claim")]
    [DataRow(DatabaseProvider.MySql, "claim")]
    [DataRow(DatabaseProvider.SqlServer, "tenant")]
    [DataRow(DatabaseProvider.MySql, "tenant")]
    public async Task Cycle_freezes_cutoff_until_completion(DatabaseProvider provider, string kind)
    {
        var f = new Fixture(provider, kind);
        await f.RunAsync(); f.Clock.UtcNow = f.Clock.UtcNow.AddHours(2); await f.RunAsync();
        var key = kind == "claim" ? "StaleBeforeUtc" : "CreatedBeforeUtc";
        Assert.AreEqual(f.Reads[0][key], f.Reads[1][key], "持续新增记录不能无限延长当前环扫。");
    }

    [TestMethod]
    [DataRow("upload", 0, 1, 30, 5)]
    [DataRow("claim", 0, 1, 30, 5)]
    [DataRow("tenant", 0, 1, 30, 5)]
    [DataRow("upload", 1, 0, 30, 5)]
    [DataRow("claim", 1, 0, 30, 5)]
    [DataRow("tenant", 1, 0, 30, 5)]
    [DataRow("upload", 1, 1, -1, 5)]
    [DataRow("claim", 1, 1, -1, 5)]
    [DataRow("tenant", 1, 1, -1, 5)]
    [DataRow("upload", 1, 1, 30, 4)]
    [DataRow("claim", 1, 1, 30, 4)]
    [DataRow("tenant", 1, 1, 30, 4)]
    [DataRow("upload", 1001, 1, 30, 5)]
    [DataRow("claim", 1001, 1, 30, 5)]
    [DataRow("tenant", 1001, 1, 30, 5)]
    [DataRow("upload", 1, 101, 30, 5)]
    [DataRow("claim", 1, 101, 30, 5)]
    [DataRow("tenant", 1, 101, 30, 5)]
    [DataRow("upload", 1, 1, 86401, 5)]
    [DataRow("claim", 1, 1, 86401, 5)]
    [DataRow("tenant", 1, 1, 86401, 5)]
    [DataRow("upload", 1, 1, 30, 86401)]
    [DataRow("claim", 1, 1, 30, 86401)]
    [DataRow("tenant", 1, 1, 30, 86401)]
    public async Task Invalid_runtime_options_fail_before_queries(string kind, int size, int batches, int age, int poll)
    {
        var f = new Fixture(DatabaseProvider.SqlServer, kind);
        await Assert.ThrowsAsync<OptionsValidationException>(() => f.RunAsync(size, batches, age, poll));
        Assert.HasCount(0, f.Reads); Assert.AreEqual(0, f.Commands);
    }

    [TestMethod]
    [DataRow(29)]
    [DataRow(86401)]
    public async Task Claim_release_grace_bounds_are_enforced_before_queries(int grace)
    {
        var f = new Fixture(DatabaseProvider.SqlServer, "claim");
        await Assert.ThrowsAsync<OptionsValidationException>(() => f.RunAsync(grace: grace));
        Assert.HasCount(0, f.Reads); Assert.AreEqual(0, f.Commands);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "upload")]
    [DataRow(DatabaseProvider.MySql, "upload")]
    [DataRow(DatabaseProvider.SqlServer, "claim")]
    [DataRow(DatabaseProvider.MySql, "claim")]
    [DataRow(DatabaseProvider.SqlServer, "tenant")]
    [DataRow(DatabaseProvider.MySql, "tenant")]
    public async Task Cancelled_probe_preserves_retry_position_and_tenant_context(DatabaseProvider provider, string kind)
    {
        using var cancelled = new CancellationTokenSource(); var f = new Fixture(provider, kind);
        f.OnProbe = cancelled.Cancel;
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.RunAsync(ct: cancelled.Token));
        f.OnProbe = null; await f.RunAsync();
        Assert.IsNull(f.Reads[0]["AfterId"]); Assert.IsNull(f.Reads[1]["AfterId"]);
        Assert.AreEqual(0, f.Commands); Assert.IsNull(f.Tenant.Id);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "upload")]
    [DataRow(DatabaseProvider.MySql, "upload")]
    [DataRow(DatabaseProvider.SqlServer, "claim")]
    [DataRow(DatabaseProvider.MySql, "claim")]
    [DataRow(DatabaseProvider.SqlServer, "tenant")]
    [DataRow(DatabaseProvider.MySql, "tenant")]
    public async Task Tightened_age_window_never_expands_current_scan(DatabaseProvider provider, string kind)
    {
        var f = new Fixture(provider, kind); await f.RunAsync(); await f.RunAsync(age: 86400);
        var key = kind == "claim" ? "StaleBeforeUtc" : "CreatedBeforeUtc";
        Assert.IsTrue((DateTimeOffset)f.Reads[1][key]! < (DateTimeOffset)f.Reads[0][key]!);
        Assert.AreEqual(0, f.Commands);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "upload")]
    [DataRow(DatabaseProvider.MySql, "upload")]
    [DataRow(DatabaseProvider.SqlServer, "claim")]
    [DataRow(DatabaseProvider.MySql, "claim")]
    [DataRow(DatabaseProvider.SqlServer, "tenant")]
    [DataRow(DatabaseProvider.MySql, "tenant")]
    public async Task Database_failure_retries_unfinished_page(DatabaseProvider provider, string kind)
    {
        var f = new Fixture(provider, kind); f.Rows.RemoveAt(0); f.CommandFailure = true;
        if (kind == "tenant") Assert.AreEqual(0, (await f.RunAsync()).Written);
        else await Assert.ThrowsAsync<IOException>(() => f.RunAsync());
        f.CommandFailure = false;
        // 租户链路单项失败按既有策略继续环扫，周期结束后再重试；其他链路保留未完成页。
        if (kind == "tenant") Assert.AreEqual(0, (await f.RunAsync()).Scanned);
        Assert.AreEqual(1, (await f.RunAsync()).Written); Assert.IsNull(f.Tenant.Id);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "upload")]
    [DataRow(DatabaseProvider.MySql, "upload")]
    [DataRow(DatabaseProvider.SqlServer, "tenant")]
    [DataRow(DatabaseProvider.MySql, "tenant")]
    public async Task Cancelled_owner_read_never_starts_a_write(DatabaseProvider provider, string kind)
    {
        using var cancelled = new CancellationTokenSource(); var f = new Fixture(provider, kind);
        f.Rows.RemoveAt(0); f.ForceMissing = true; f.OnOwner = cancelled.Cancel;
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.RunAsync(ct: cancelled.Token));
        Assert.AreEqual(0, f.Commands, "权威端口返回后已取消时不得继续发起数据写入。");
        Assert.IsNull(f.Tenant.Id);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Null_probe_answer_is_retained_without_blocking_next_page(DatabaseProvider provider)
    {
        var f = new Fixture(provider, "claim") { NullProbe = true };
        Assert.AreEqual(0, (await f.RunAsync()).Written);
        Assert.AreEqual(1, (await f.RunAsync()).Written); Assert.HasCount(1, f.Rows);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "ready")]
    [DataRow(DatabaseProvider.MySql, "ready")]
    [DataRow(DatabaseProvider.SqlServer, "resolver")]
    [DataRow(DatabaseProvider.MySql, "resolver")]
    public async Task Tenant_authority_cancellation_never_releases_or_enters_a_cancelled_context(DatabaseProvider provider, string stage)
    {
        using var cancelled = new CancellationTokenSource(); var f = new Fixture(provider, "tenant"); f.Rows.RemoveAt(0);
        if (stage == "ready")
        {
            var r = f.Rows[0]; f.Rows[0] = (r.Id, r.At, ((TenantResourceFileReconciliationRecord)r.Row) with { StatusKey = "ready" });
            f.OnOwner = cancelled.Cancel;
        }
        else f.OnResolve = cancelled.Cancel;
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.RunAsync(ct: cancelled.Token));
        Assert.AreEqual(0, f.Commands); Assert.IsNull(f.Tenant.Id);
    }

    [TestMethod]
    [DataRow("upload")]
    [DataRow("claim")]
    [DataRow("tenant")]
    public async Task Cancelled_waiter_never_enters_or_releases_another_scan(string kind)
    {
        var f = new Fixture(DatabaseProvider.SqlServer, kind) { ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var first = f.RunAsync();
        try
        {
            await f.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); using var cancelled = new CancellationTokenSource();
            var second = f.RunAsync(ct: cancelled.Token); cancelled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => second); Assert.AreEqual(1, f.ReadCalls);
        }
        finally { f.ReadGate.TrySetResult(); await first; }
        Assert.AreEqual(1, (await f.RunAsync()).Written); Assert.AreEqual(2, f.ReadCalls);
    }

    private sealed class MutableClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
    private sealed record Counts(int Scanned, int Written);
    private sealed class Fixture
    {
        internal MutableClock Clock { get; } = new();
        internal CurrentTenantAccessor Tenant { get; } = new();
        internal List<(Guid Id, DateTimeOffset At, object Row)> Rows { get; } = [];
        internal List<IReadOnlyDictionary<string, object?>> Reads { get; } = [];
        internal int Commands { get; private set; }
        internal Action? OnProbe { get; set; }
        internal Action? OnOwner { get; set; }
        internal bool ForceMissing { get; set; }
        internal bool CommandFailure { get; set; }
        internal bool NullProbe { get; set; }
        internal Action? OnResolve { get; set; }
        internal TaskCompletionSource? ReadGate { get; set; }
        internal TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int ReadCalls { get; private set; }
        private readonly object _runner;

        internal Fixture(DatabaseProvider provider, string kind)
        {
            var tenantId = Guid.CreateVersion7(); var badId = Guid.CreateVersion7(); var goodId = Guid.CreateVersion7();
            foreach (var (id, key, at) in new[] { (badId, "failed", Clock.UtcNow.AddHours(-2)), (goodId, "later", Clock.UtcNow.AddHours(-1)) })
            {
                object row = kind switch
                {
                    "upload" => new PendingHostFileRecord(id, "local", key, at),
                    "claim" => new HostFileReferenceClaimRecord(id, key, Guid.CreateVersion7(), "document", id, "pending", null, 1, at, at, null, null),
                    _ => new TenantResourceFileReconciliationRecord(id, tenantId, "reporting", Guid.CreateVersion7(), "local", key, "pending", at),
                };
                Rows.Add((id, at, row));
            }
            var query = Substitute.For<IQueryExecutor>();
            query.QueryAsync<PendingHostFileRecord>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(c => ReadAsync<PendingHostFileRecord>(c[1]!));
            query.QueryAsync<HostFileReferenceClaimRecord>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(c => ReadAsync<HostFileReferenceClaimRecord>(c[1]!));
            query.QueryAsync<TenantResourceFileReconciliationRecord>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(c => ReadAsync<TenantResourceFileReconciliationRecord>(c[1]!));
            var command = Substitute.For<ICommandExecutor>();
            command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(c =>
            {
                Commands++; var p = (IReadOnlyDictionary<string, object?>)c[1]!;
                if (CommandFailure) return Task.FromException<int>(new IOException("数据库故障"));
                var id = (Guid)(p.TryGetValue("FileId", out var file) ? file! : p["Id"]!);
                return Task.FromResult(Rows.RemoveAll(r => r.Id == id));
            });
            var blobs = Substitute.For<IFileStorageProvider>(); blobs.ProviderKey.Returns("local");
            blobs.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(c =>
            {
                OnProbe?.Invoke();
                return c.ArgAt<string>(0) == "failed" ? Task.FromException<bool>(new IOException("首批对象探测失败")) : Task.FromResult(!ForceMissing);
            });
            var storage = new FileStorageProviderRegistry([blobs], Options.Create(new FileStorageOptions()));
            var database = Options.Create(new DatabaseOptions { Provider = provider });
            var probe = Substitute.For<IHostFileReferenceClaimProbe>(); probe.ConsumerModule.Returns("document");
            probe.ProbeReferenceAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(c =>
            {
                OnProbe?.Invoke();
                if (NullProbe && c.ArgAt<Guid>(0) == badId) return Task.FromResult<HostFileReferenceClaimProbeResult>(null!);
                return Task.FromResult(new HostFileReferenceClaimProbeResult(c.ArgAt<Guid>(0) == badId ? HostFileReferenceClaimProbeOutcome.Failed : HostFileReferenceClaimProbeOutcome.NotFound));
            });
            var retention = Substitute.For<IHostFileRetentionContributor>();
            retention.IsFileReferencedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => { OnOwner?.Invoke(); return Task.FromResult(false); });
            var owner = Substitute.For<ITenantResourceFileOwner>(); owner.OwnerModuleKey.Returns("reporting");
            owner.IsReferencedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => { OnOwner?.Invoke(); return Task.FromResult(false); });
            var resolver = Substitute.For<IActiveTenantContextResolver>();
            resolver.ResolveActiveByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(_ => { OnResolve?.Invoke(); return Task.FromResult<TenantContext?>(new(tenantId, "cycle", "周期")); });
            _runner = kind switch
            {
                "upload" => new PendingHostFileReconciliationRunner(query, command, storage, [retention], database, Clock),
                "claim" => new PendingHostFileReferenceClaimReconciliationRunner(query, command, [probe], database, Clock),
                _ => new PendingTenantResourceFileReconciliationRunner(query, command, storage, [owner], resolver, Tenant, database, Clock),
            };
        }

        private object[] Read(object parameters)
        {
            var p = (IReadOnlyDictionary<string, object?>)parameters; Reads.Add(p);
            var claim = p.ContainsKey("StaleBeforeUtc"); var cutoff = (DateTimeOffset)p[claim ? "StaleBeforeUtc" : "CreatedBeforeUtc"]!;
            var after = (DateTimeOffset)p[claim ? "AfterUpdatedAtUtc" : "AfterCreatedAtUtc"]!;
            return Rows.Where(r => r.At <= cutoff && ((int)p["HasCursor"]! == 0 || r.At > after))
                .OrderBy(r => r.At).Take((int)p["BatchSize"]!).Select(r => r.Row).ToArray();
        }

        private async Task<IReadOnlyList<T>> ReadAsync<T>(object parameters)
        {
            ReadCalls++; ReadEntered.TrySetResult(); if (ReadGate is { } gate) await gate.Task;
            return Read(parameters).Cast<T>().ToArray();
        }

        internal async Task<Counts> RunAsync(int size = 1, int batches = 1, int age = 30, int poll = 5, int grace = 900, CancellationToken ct = default)
        {
            switch (_runner)
            {
                case PendingHostFileReconciliationRunner upload:
                    var a = await upload.RunOnceAsync(new() { Enabled = true, BatchSize = size, MaxBatchesPerRun = batches, MinimumAgeSeconds = age, PollSeconds = poll }, ct);
                    return new(a.Scanned, a.Promoted + a.Purged);
                case PendingHostFileReferenceClaimReconciliationRunner claim:
                    var b = await claim.RunOnceAsync(new() { Enabled = true, BatchSize = size, MaxBatchesPerRun = batches, MinimumAgeSeconds = age, PollSeconds = poll, ReleaseGraceSeconds = grace }, ct);
                    return new(b.Scanned, b.Promoted + b.Released);
                default:
                    var c = await ((PendingTenantResourceFileReconciliationRunner)_runner).RunOnceAsync(new() { Enabled = true, BatchSize = size, MaxBatchesPerRun = batches, MinimumAgeSeconds = age, PollSeconds = poll }, ct);
                    return new(c.Scanned, c.Promoted + c.Purged + c.Released);
            }
        }
    }
}
