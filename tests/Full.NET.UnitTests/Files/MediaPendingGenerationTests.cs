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

/// <summary>同文件的另一代际不能证明失败绑定有效；兼容旧键仍以当前业务绑定为准。</summary>
[TestClass]
public sealed class MediaPendingGenerationTests
{
    [TestMethod]
    [DataRow("avatar", "match")]
    [DataRow("signature", "match")]
    [DataRow("logo", "match")]
    [DataRow("avatar", "other-generation")]
    [DataRow("signature", "other-generation")]
    [DataRow("logo", "other-generation")]
    [DataRow("avatar", "legacy")]
    [DataRow("signature", "legacy")]
    [DataRow("logo", "legacy")]
    [DataRow("avatar", "retired-legacy")]
    [DataRow("signature", "retired-legacy")]
    [DataRow("logo", "retired-legacy")]
    public async Task Media_probe_matches_only_effective_current_key(string kind, string scenario)
    {
        var subject = Guid.CreateVersion7(); var file = Guid.CreateVersion7(); var query = Substitute.For<IQueryExecutor>();
        var legacy = Key(kind, subject, file, null); var current = scenario == "legacy" ? null : Key(kind, subject, file, Guid.CreateVersion7());
        var scanned = scenario switch { "match" => current!, "other-generation" => Key(kind, subject, file, Guid.CreateVersion7()), _ => legacy };
        query.QuerySingleOrDefaultAsync<int>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        query.QueryAsync<HostUserProfileRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<HostUserProfileRecord>>([new() { UserId = subject, AvatarFileId = kind == "avatar" ? file : null,
                SignatureFileId = kind == "signature" ? file : null, AvatarClaimKey = kind == "avatar" ? current : null, SignatureClaimKey = kind == "signature" ? current : null }]));
        query.QuerySingleOrDefaultAsync<TenantBrandingRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(
            new TenantBrandingRecord(subject, null, file, null, null, null, null, 1, current));
        IHostFileReferenceBindingClaimProbe probe = kind == "logo" ? new TenancyTenantLogoReferenceProbe(query) : new IdentityUserProfileMediaReferenceProbe(query);
        var result = await probe.ProbeBindingAsync(subject, file, scanned);
        Assert.AreEqual(scenario is "match" or "legacy" ? HostFileReferenceClaimProbeOutcome.Exists : HostFileReferenceClaimProbeOutcome.NotFound, result.Outcome);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, HostFileReferenceClaimProbeOutcome.Exists)]
    [DataRow(DatabaseProvider.MySql, HostFileReferenceClaimProbeOutcome.Exists)]
    [DataRow(DatabaseProvider.SqlServer, HostFileReferenceClaimProbeOutcome.NotFound)]
    [DataRow(DatabaseProvider.MySql, HostFileReferenceClaimProbeOutcome.NotFound)]
    [DataRow(DatabaseProvider.SqlServer, HostFileReferenceClaimProbeOutcome.Failed)]
    [DataRow(DatabaseProvider.MySql, HostFileReferenceClaimProbeOutcome.Failed)]
    public async Task Pending_runner_uses_scanned_key_for_generation_aware_consumers(DatabaseProvider provider, HostFileReferenceClaimProbeOutcome outcome)
    {
        var now = DateTimeOffset.UtcNow; var record = new HostFileReferenceClaimRecord(Guid.CreateVersion7(), "scanned-binding-key", Guid.CreateVersion7(),
            "identity", Guid.CreateVersion7(), "pending", null, 1, now.AddHours(-2), now.AddHours(-1), null, null);
        var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<HostFileReferenceClaimRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<HostFileReferenceClaimRecord>>([record]));
        var probe = Substitute.For<IHostFileReferenceBindingClaimProbe>(); probe.ConsumerModule.Returns("identity");
        probe.ProbeReferenceAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new HostFileReferenceClaimProbeResult(HostFileReferenceClaimProbeOutcome.Exists));
        probe.ProbeBindingAsync(record.ConsumerReferenceId, record.FileId, record.IdempotencyKey, Arg.Any<CancellationToken>()).Returns(new HostFileReferenceClaimProbeResult(outcome));
        var command = Substitute.For<ICommandExecutor>(); command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
        var runner = new PendingHostFileReferenceClaimReconciliationRunner(query, command, [probe], Options.Create(new DatabaseOptions { Provider = provider }), clock);
        var result = await runner.RunOnceAsync(new PendingHostFileReferenceClaimReconciliationOptions { MaxBatchesPerRun = 1 }, default);
        Assert.AreEqual(outcome == HostFileReferenceClaimProbeOutcome.Exists ? 1 : 0, result.Promoted);
        Assert.AreEqual(outcome == HostFileReferenceClaimProbeOutcome.NotFound ? 1 : 0, result.Released);
        Assert.AreEqual(outcome == HostFileReferenceClaimProbeOutcome.Failed ? 1 : 0, result.ProbeFailures);
        await probe.Received(1).ProbeBindingAsync(record.ConsumerReferenceId, record.FileId, record.IdempotencyKey, Arg.Any<CancellationToken>());
        await probe.DidNotReceive().ProbeReferenceAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private static string Key(string kind, Guid subject, Guid file, Guid? generation) => (kind, generation) switch
    {
        ("avatar", Guid id) => HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(subject, file, id),
        ("signature", Guid id) => HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(subject, file, id),
        ("logo", Guid id) => HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(subject, file, id),
        ("avatar", _) => HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(subject, file),
        ("signature", _) => HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(subject, file),
        _ => HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(subject, file),
    };
}
