using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Features.HostFileReferences;
using Full.NET.Modules.Tenancy.Features.HostFileReferences;
using Full.NET.Serialization.MemoryPack;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>消费已退役代际，不根据当前绑定推导键；失败和取消不能伪装为已确认。</summary>
[TestClass]
public sealed class MediaReleaseConsumerTests
{
    [TestMethod]
    [DataRow("avatar")]
    [DataRow("signature")]
    [DataRow("logo")]
    public async Task Failure_retry_and_duplicate_delivery_release_only_the_retired_generation(string kind)
    {
        var f = new Fixture(kind); var states = new Dictionary<string, string> { [f.Key] = "active", [f.NewKey] = "active" }; var attempt = 0;
        f.Claims.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            if (++attempt == 1) throw new IOException("offline");
            states[call.Arg<string>()!] = "released"; return Result<bool>.Success(true);
        });
        await Assert.ThrowsExactlyAsync<IOException>(() => f.HandleAsync());
        await f.HandleAsync(); await f.HandleAsync();
        Assert.AreEqual("released", states[f.Key]); Assert.AreEqual("active", states[f.NewKey]);
        await f.Claims.Received(3).ReleaseAsync(f.Key, Arg.Any<CancellationToken>());
        Assert.AreEqual(IntegrationEventIdempotencyStrategy.NaturallyIdempotent, f.Handler.IdempotencyStrategy);
    }

    [TestMethod]
    [DataRow("avatar")]
    [DataRow("signature")]
    [DataRow("logo")]
    public async Task Missing_historical_claim_is_terminal(string kind)
    {
        var f = new Fixture(kind);
        f.Claims.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            Result<bool>.Failure(new Error(FilesErrorCodes.ClaimNotFound, "missing", ErrorType.NotFound)));
        await f.HandleAsync();
    }

    [TestMethod]
    [DataRow("avatar")]
    [DataRow("signature")]
    [DataRow("logo")]
    public async Task Legacy_binding_fact_remains_readable(string kind)
    {
        var f = new Fixture(kind);
        await f.Handler.HandleAsync(f.Context, f.Payload("legacy"), default);
        await f.Claims.Received(1).ReleaseAsync(f.LegacyKey, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Unknown_media_kind_cannot_release_a_claim()
    {
        var f = new Fixture("avatar");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.Handler.HandleAsync(f.Context, f.Payload("kind"), default));
        await f.Claims.DidNotReceive().ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("avatar", "tenant")]
    [DataRow("signature", "tenant")]
    [DataRow("logo", "tenant")]
    [DataRow("avatar", "type")]
    [DataRow("signature", "type")]
    [DataRow("logo", "type")]
    [DataRow("avatar", "version")]
    [DataRow("signature", "version")]
    [DataRow("logo", "version")]
    [DataRow("avatar", "message-id")]
    [DataRow("signature", "message-id")]
    [DataRow("logo", "message-id")]
    [DataRow("avatar", "subject")]
    [DataRow("signature", "subject")]
    [DataRow("logo", "subject")]
    [DataRow("avatar", "file")]
    [DataRow("signature", "file")]
    [DataRow("logo", "file")]
    [DataRow("avatar", "claim-key")]
    [DataRow("signature", "claim-key")]
    [DataRow("logo", "claim-key")]
    public async Task Invalid_envelope_or_payload_cannot_release_claims(string kind, string change)
    {
        var f = new Fixture(kind); var context = f.Context;
        context = change switch
        {
            "tenant" => context with { TenantId = Guid.CreateVersion7() },
            "type" => context with { MessageType = "fullnet.other.unbound" },
            "version" => context with { SchemaVersion = 2 },
            "message-id" => context with { MessageId = Guid.Empty },
            _ => context,
        };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.Handler.HandleAsync(context, f.Payload(change), default));
        await f.Claims.DidNotReceive().ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("avatar")]
    [DataRow("signature")]
    [DataRow("logo")]
    public async Task Successful_false_result_is_not_acknowledged(string kind)
    {
        var f = new Fixture(kind); f.Claims.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(false));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.HandleAsync());
    }

    [TestMethod]
    [DataRow("avatar")]
    [DataRow("signature")]
    [DataRow("logo")]
    public async Task Cancellation_before_release_has_no_side_effect(string kind)
    {
        var f = new Fixture(kind); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.HandleAsync(cancellation.Token));
        await f.Claims.DidNotReceive().ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("avatar")]
    [DataRow("signature")]
    [DataRow("logo")]
    public async Task Cancellation_after_committed_release_is_not_acknowledged(string kind)
    {
        var f = new Fixture(kind); using var cancellation = new CancellationTokenSource();
        f.Claims.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => { cancellation.Cancel(); return Result<bool>.Success(true); });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.HandleAsync(cancellation.Token));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Tenant_claim_release_runs_as_host_and_restores_tenant_on_success_or_failure(bool fail)
    {
        var f = new Fixture("logo");
        f.Claims.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Assert.IsTrue(f.Writer.IsHost); if (fail) throw new IOException("offline"); return Result<bool>.Success(true);
        });
        if (fail) await Assert.ThrowsExactlyAsync<IOException>(() => f.HandleAsync()); else await f.HandleAsync();
        Assert.IsFalse(f.Writer.IsHost); Assert.AreEqual(f.Subject, f.Writer.Id);
    }

    private sealed class Fixture
    {
        internal readonly Guid Subject = Guid.CreateVersion7(), File = Guid.CreateVersion7();
        internal readonly IHostFileReferenceClaimService Claims = Substitute.For<IHostFileReferenceClaimService>();
        internal readonly ICurrentTenantContextWriter Writer = Substitute.For<ICurrentTenantContextWriter>();
        private readonly IIntegrationEventSerializer serializer = new MemoryPackIntegrationEventSerializer();
        private readonly string kind;
        internal readonly string Key, NewKey;
        internal string LegacyKey => kind switch
        {
            "avatar" => HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(Subject, File),
            "signature" => HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(Subject, File),
            _ => HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(Subject, File),
        };
        internal readonly IIntegrationEventHandler Handler;
        internal readonly IntegrationEventContext Context;
        internal Fixture(string kind)
        {
            this.kind = kind;
            Key = CreateKey(Guid.CreateVersion7()); NewKey = CreateKey(Guid.CreateVersion7());
            Guid? current = kind == "logo" ? Subject : null; var host = kind != "logo";
            Writer.Id.Returns(_ => current); Writer.IsHost.Returns(_ => host); Writer.Identifier.Returns("media"); Writer.Name.Returns("媒体");
            Writer.When(writer => writer.SetHost()).Do(_ => { current = null; host = true; });
            Writer.When(writer => writer.SetTenant(Arg.Any<TenantContext>())).Do(call => { current = call.Arg<TenantContext>()!.Id; host = false; });
            Handler = kind == "logo" ? new TenantLogoUnboundIntegrationEventHandler(serializer, Writer, Claims)
                : new IdentityProfileMediaUnboundIntegrationEventHandler(serializer, Claims);
            Context = new(Guid.CreateVersion7(), Handler.EventType, 1, kind == "logo" ? Subject : null, null, DateTimeOffset.UtcNow);
            Claims.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        }
        private string CreateKey(Guid id) => kind switch
        {
            "avatar" => HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(Subject, File, id),
            "signature" => HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(Subject, File, id),
            _ => HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(Subject, File, id),
        };
        internal byte[] Payload(string change = "")
        {
            var subject = change == "subject" ? Guid.Empty : Subject; var file = change == "file" ? Guid.Empty : File;
            var key = change == "claim-key" ? "document-version:" + Guid.CreateVersion7().ToString("D") : change == "legacy" ? LegacyKey : Key;
            return kind == "logo" ? serializer.Serialize(new TenantLogoUnboundIntegrationEvent(subject, file, key))
                : serializer.Serialize(new IdentityProfileMediaUnboundIntegrationEvent(subject, file, change == "kind" ? "other" : kind, key));
        }
        internal Task HandleAsync(CancellationToken ct = default) => Handler.HandleAsync(Context, Payload(), ct);
    }
}
