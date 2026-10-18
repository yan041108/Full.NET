using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;
using Full.NET.Modules.Files.Reconciliation;
using Full.NET.Modules.Identity.Features.HostFileReferences;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Tenancy.Features.HostFileReferences;
using Full.NET.Modules.Tenancy.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

/// <summary>可复用媒体只补偿永久退休的代际；当前、历史旧键及不可信元数据必须保留。</summary>
[TestClass]
public sealed class MediaActiveGenerationTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "avatar")]
    [DataRow(DatabaseProvider.MySql, "avatar")]
    [DataRow(DatabaseProvider.SqlServer, "signature")]
    [DataRow(DatabaseProvider.MySql, "signature")]
    [DataRow(DatabaseProvider.SqlServer, "logo")]
    [DataRow(DatabaseProvider.MySql, "logo")]
    public async Task Active_runner_releases_only_retired_generation(DatabaseProvider provider, string kind)
    {
        var f = new Fixture(kind, "retired");
        var retired = f.Row(f.ScannedKey); var current = f.Row(f.CurrentKey!); var legacy = f.Row(f.LegacyKey);
        f.Query.QueryAsync<HostFileReferenceClaimRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<HostFileReferenceClaimRecord>>([retired, current, legacy]));
        var released = new List<Guid>(); var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var parameters = (Dictionary<string, object?>)call.ArgAt<object>(1);
            released.Add((Guid)parameters["Id"]!); return Task.FromResult(1);
        });
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(f.Now);
        var runner = new ActiveHostFileReferenceClaimReconciliationRunner(f.Query, command, [f.Probe],
            Options.Create(new DatabaseOptions { Provider = provider }), clock);
        var result = await runner.RunOnceAsync(new() { MaxBatchesPerRun = 1 }, default);
        Assert.AreEqual(3, result.Scanned); Assert.AreEqual(1, result.Released); Assert.AreEqual(1, result.ProbeFailures);
        CollectionAssert.AreEqual(new[] { retired.Id }, released);
        await f.AssertOneBatchQueryAsync();
    }

    [TestMethod]
    [DataRow("avatar", "current", HostFileReferenceClaimProbeOutcome.Exists)]
    [DataRow("signature", "current", HostFileReferenceClaimProbeOutcome.Exists)]
    [DataRow("logo", "current", HostFileReferenceClaimProbeOutcome.Exists)]
    [DataRow("avatar", "retired", HostFileReferenceClaimProbeOutcome.NotFound)]
    [DataRow("signature", "retired", HostFileReferenceClaimProbeOutcome.NotFound)]
    [DataRow("logo", "retired", HostFileReferenceClaimProbeOutcome.NotFound)]
    [DataRow("avatar", "missing", HostFileReferenceClaimProbeOutcome.NotFound)]
    [DataRow("signature", "missing", HostFileReferenceClaimProbeOutcome.NotFound)]
    [DataRow("logo", "missing", HostFileReferenceClaimProbeOutcome.NotFound)]
    [DataRow("avatar", "legacy-current", HostFileReferenceClaimProbeOutcome.Failed)]
    [DataRow("signature", "legacy-current", HostFileReferenceClaimProbeOutcome.Failed)]
    [DataRow("logo", "legacy-current", HostFileReferenceClaimProbeOutcome.Failed)]
    [DataRow("avatar", "unknown-key", HostFileReferenceClaimProbeOutcome.Failed)]
    [DataRow("signature", "unknown-key", HostFileReferenceClaimProbeOutcome.Failed)]
    [DataRow("logo", "unknown-key", HostFileReferenceClaimProbeOutcome.Failed)]
    [DataRow("avatar", "corrupt-key", HostFileReferenceClaimProbeOutcome.Failed)]
    [DataRow("signature", "corrupt-key", HostFileReferenceClaimProbeOutcome.Failed)]
    [DataRow("logo", "corrupt-key", HostFileReferenceClaimProbeOutcome.Failed)]
    [DataRow("avatar", "old-request", HostFileReferenceClaimProbeOutcome.Failed)]
    [DataRow("signature", "old-request", HostFileReferenceClaimProbeOutcome.Failed)]
    [DataRow("logo", "old-request", HostFileReferenceClaimProbeOutcome.Failed)]
    [DataRow("avatar", "different-file", HostFileReferenceClaimProbeOutcome.NotFound)]
    [DataRow("signature", "different-file", HostFileReferenceClaimProbeOutcome.NotFound)]
    [DataRow("logo", "different-file", HostFileReferenceClaimProbeOutcome.NotFound)]
    [DataRow("avatar", "corrupt-other-file", HostFileReferenceClaimProbeOutcome.Failed)]
    [DataRow("signature", "corrupt-other-file", HostFileReferenceClaimProbeOutcome.Failed)]
    [DataRow("logo", "corrupt-other-file", HostFileReferenceClaimProbeOutcome.Failed)]
    [DataRow("avatar", "cleared", HostFileReferenceClaimProbeOutcome.NotFound)]
    [DataRow("signature", "cleared", HostFileReferenceClaimProbeOutcome.NotFound)]
    [DataRow("logo", "cleared", HostFileReferenceClaimProbeOutcome.NotFound)]
    [DataRow("avatar", "dangling-key", HostFileReferenceClaimProbeOutcome.Failed)]
    [DataRow("signature", "dangling-key", HostFileReferenceClaimProbeOutcome.Failed)]
    [DataRow("logo", "dangling-key", HostFileReferenceClaimProbeOutcome.Failed)]
    public async Task Batch_probe_requires_authoritative_retirement(string kind, string scenario, HostFileReferenceClaimProbeOutcome expected)
    {
        var f = new Fixture(kind, scenario); var claim = Guid.CreateVersion7();
        Assert.IsInstanceOfType<IHostImmutableFileBindingClaimProbe>(f.Probe);
        var results = await ((IHostImmutableFileBindingClaimProbe)f.Probe).ProbeBindingsAsync([new(claim, f.Subject, f.File, f.ScannedKey)]);
        Assert.AreEqual(expected, results[claim].Outcome);
        if (scenario == "old-request") Assert.AreEqual(0, f.Query.ReceivedCalls().Count());
        else await f.AssertOneBatchQueryAsync();
    }

    [TestMethod]
    [DataRow("avatar", "duplicate")]
    [DataRow("signature", "duplicate")]
    [DataRow("logo", "duplicate")]
    [DataRow("avatar", "invalid-id")]
    [DataRow("signature", "invalid-id")]
    [DataRow("logo", "invalid-id")]
    [DataRow("avatar", "foreign-key")]
    [DataRow("signature", "foreign-key")]
    [DataRow("logo", "foreign-key")]
    [DataRow("avatar", "query-error")]
    [DataRow("signature", "query-error")]
    [DataRow("logo", "query-error")]
    public async Task Invalid_or_failed_batch_preserves_claims(string kind, string scenario)
    {
        var f = new Fixture(kind, "retired"); var id = Guid.CreateVersion7();
        var request = new HostFileBindingClaimProbeRequest(id, f.Subject, scenario == "invalid-id" ? Guid.Empty : f.File,
            scenario == "foreign-key" ? Key(kind, Guid.CreateVersion7(), f.File, Guid.CreateVersion7()) : f.ScannedKey);
        if (scenario == "query-error") f.FailRead();
        var results = await ((IHostImmutableFileBindingClaimProbe)f.Probe).ProbeBindingsAsync(scenario == "duplicate" ? [request, request] : [request]);
        Assert.AreEqual(HostFileReferenceClaimProbeOutcome.Failed, results[id].Outcome);
        if (scenario == "query-error") await f.AssertOneBatchQueryAsync();
        else Assert.AreEqual(0, f.Query.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("avatar")]
    [DataRow("signature")]
    [DataRow("logo")]
    public async Task Cancellation_after_read_never_returns_retirement(string kind)
    {
        var f = new Fixture(kind, "retired"); using var cancelled = new CancellationTokenSource();
        f.CancelRead(cancelled);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => ((IHostImmutableFileBindingClaimProbe)f.Probe)
            .ProbeBindingsAsync([new(Guid.CreateVersion7(), f.Subject, f.File, f.ScannedKey)], cancelled.Token));
    }

    private sealed class Fixture
    {
        internal readonly Guid Subject = Guid.CreateVersion7(), File = Guid.CreateVersion7();
        internal readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
        internal readonly IQueryExecutor Query = Substitute.For<IQueryExecutor>();
        internal readonly string LegacyKey, ScannedKey;
        internal readonly string? CurrentKey;
        internal readonly IHostFileReferenceClaimProbe Probe;
        private readonly string _kind;

        internal Fixture(string kind, string scenario)
        {
            _kind = kind; LegacyKey = Key(kind, Subject, File, null);
            Guid? boundFile = scenario is "cleared" or "dangling-key" ? null : scenario is "different-file" or "corrupt-other-file" ? Guid.CreateVersion7() : File;
            CurrentKey = scenario switch { "unknown-key" or "cleared" => null, "legacy-current" => LegacyKey, "corrupt-key" or "corrupt-other-file" => "untrusted-key", _ => Key(kind, Subject, boundFile ?? File, Guid.CreateVersion7()) };
            ScannedKey = scenario switch { "current" => CurrentKey!, "old-request" => LegacyKey, _ => Key(kind, Subject, File, Guid.CreateVersion7()) };
            var profile = new HostUserProfileRecord { UserId = Subject, AvatarFileId = kind == "avatar" ? boundFile : null,
                SignatureFileId = kind == "signature" ? boundFile : null, AvatarClaimKey = kind == "avatar" ? CurrentKey : null,
                SignatureClaimKey = kind == "signature" ? CurrentKey : null };
            Query.QueryAsync<HostUserProfileRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<HostUserProfileRecord>>(scenario == "missing" ? [] : [profile]));
            var branding = new TenantBrandingRecord(Subject, null, boundFile, null, null, null, null, 1, CurrentKey);
            Query.QueryAsync<TenantBrandingRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<TenantBrandingRecord>>(scenario == "missing" ? [] : [branding]));
            Probe = kind == "logo" ? new TenancyTenantLogoReferenceProbe(Query) : new IdentityUserProfileMediaReferenceProbe(Query);
        }

        internal HostFileReferenceClaimRecord Row(string key) => new(Guid.CreateVersion7(), key, File,
            _kind == "logo" ? "tenancy" : "identity", Subject, "active", null, 1, Now.AddHours(-2), Now.AddHours(-1), Now.AddHours(-1), null);

        internal async Task AssertOneBatchQueryAsync()
        {
            if (_kind == "logo") await Query.Received(1).QueryAsync<TenantBrandingRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
            else await Query.Received(1).QueryAsync<HostUserProfileRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
            Assert.AreEqual(1, Query.ReceivedCalls().Count(call => !call.GetMethodInfo().GetGenericArguments().Contains(typeof(HostFileReferenceClaimRecord))),
                "消费方权威探测只执行一次；Files 自有扫描查询单独计数。");
        }

        internal void FailRead()
        {
            if (_kind == "logo") Query.QueryAsync<TenantBrandingRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<IReadOnlyList<TenantBrandingRecord>>(new IOException("owned probe failure")));
            else Query.QueryAsync<HostUserProfileRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<IReadOnlyList<HostUserProfileRecord>>(new IOException("owned probe failure")));
        }

        internal void CancelRead(CancellationTokenSource source)
        {
            if (_kind == "logo") Query.QueryAsync<TenantBrandingRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => { source.Cancel(); return Task.FromResult<IReadOnlyList<TenantBrandingRecord>>([]); });
            else Query.QueryAsync<HostUserProfileRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => { source.Cancel(); return Task.FromResult<IReadOnlyList<HostUserProfileRecord>>([]); });
        }
    }

    private static string Key(string kind, Guid subject, Guid file, Guid? binding) => (kind, binding) switch
    {
        ("avatar", Guid id) => HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(subject, file, id),
        ("signature", Guid id) => HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(subject, file, id),
        ("logo", Guid id) => HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(subject, file, id),
        ("avatar", _) => HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(subject, file),
        ("signature", _) => HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(subject, file),
        _ => HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(subject, file),
    };
}
