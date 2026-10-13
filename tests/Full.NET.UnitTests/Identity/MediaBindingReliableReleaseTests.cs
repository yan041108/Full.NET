using System.Diagnostics.CodeAnalysis;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>绑定与解绑事实原子提交，跨模块故障只能延迟释放，不能丢失释放任务。</summary>
public sealed partial class MediaBindingConcurrencyTests
{
    [TestMethod]
    [DataRow("avatar")]
    [DataRow("signature")]
    [DataRow("logo-host")]
    [DataRow("logo-current")]
    public async Task Committed_media_delete_retains_a_transactional_release_fact(string kind)
    {
        var f = new Fixture(kind == "signature");
        f.Claims.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            Result<bool>.Failure(new Error("test.offline", "Unavailable", ErrorType.Conflict)));
        Assert.IsTrue(await DeleteAsync(f, kind));
        Assert.HasCount(1, f.Outbox.Messages);
        Assert.IsNull(f.CurrentFile);
        var message = f.Outbox.Messages[0];
        Assert.AreEqual(kind.StartsWith("logo", StringComparison.Ordinal)
            ? "fullnet.tenancy.logo.unbound" : "fullnet.identity.profile_media.unbound", message.Type);
        Assert.AreEqual(1, message.Version);
        Assert.AreEqual(f.OldFile, message.Payload.GetType().GetProperty("FileId")!.GetValue(message.Payload));
    }

    [TestMethod]
    [DataRow("avatar")]
    [DataRow("signature")]
    [DataRow("logo-host")]
    [DataRow("logo-current")]
    public async Task Media_replacement_retains_old_release_fact_even_when_new_confirmation_fails(string kind)
    {
        var f = new Fixture(kind == "signature");
        f.Claims.ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            Result<Full.NET.Modules.Files.Contracts.HostFileReferenceClaimResult>.Failure(
                new Error("test.confirm_failed", "Unavailable", ErrorType.Conflict)));
        if (kind.StartsWith("logo", StringComparison.Ordinal)) await f.UploadLogoAsync(kind == "logo-current");
        else await f.UploadIdentityAsync();
        Assert.AreEqual(f.NewFile, f.CurrentFile);
        Assert.HasCount(1, f.Outbox.Messages);
    }

    [TestMethod]
    [DataRow("avatar")]
    [DataRow("signature")]
    [DataRow("logo-host")]
    [DataRow("logo-current")]
    public async Task Failed_release_fact_insertion_rolls_back_media_delete(string kind)
    {
        var f = new Fixture(kind == "signature"); f.Outbox.Fail = true;
        await Assert.ThrowsExactlyAsync<IOException>(() => DeleteAsync(f, kind));
        Assert.AreEqual(f.OldFile, f.CurrentFile); Assert.AreEqual(4, f.CurrentVersion);
        Assert.HasCount(0, f.Outbox.Messages);
        await f.Claims.DidNotReceive().ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.IsFalse(f.Cache.ReceivedCalls().Any());
    }

    [TestMethod]
    [DataRow("avatar")]
    [DataRow("signature")]
    [DataRow("logo-host")]
    [DataRow("logo-current")]
    public async Task Conflicting_media_delete_cannot_publish_a_release_fact(string kind)
    {
        var f = new Fixture(kind == "signature") { CurrentVersion = 5 };
        Assert.IsFalse(await DeleteAsync(f, kind)); Assert.HasCount(0, f.Outbox.Messages);
    }

    private static async Task<bool> DeleteAsync(Fixture f, string kind) => kind.StartsWith("logo", StringComparison.Ordinal)
        ? (await f.DeleteLogoAsync(kind == "logo-current")).IsSuccess : (await f.DeleteIdentityAsync()).IsSuccess;

    [TestMethod]
    [DataRow("avatar")]
    [DataRow("signature")]
    [DataRow("logo-host")]
    [DataRow("logo-current")]
    public async Task Rebinding_same_file_stores_a_fresh_generation_and_retires_only_old_key(string kind)
    {
        var f = new Fixture(kind == "signature"); f.CurrentFile = f.NewFile; f.SnapshotFile = f.NewFile;
        var id = Guid.CreateVersion7();
        var old = kind switch
        {
            "avatar" => Full.NET.Modules.Files.Contracts.HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(f.Subject, f.NewFile, id),
            "signature" => Full.NET.Modules.Files.Contracts.HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(f.Subject, f.NewFile, id),
            _ => Full.NET.Modules.Files.Contracts.HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(f.Subject, f.NewFile, id),
        };
        f.CurrentClaimKey = old;
        if (kind.StartsWith("logo", StringComparison.Ordinal)) Assert.IsTrue((await f.UploadLogoAsync(kind == "logo-current")).IsSuccess);
        else Assert.IsTrue((await f.UploadIdentityAsync()).IsSuccess);
        Assert.AreNotEqual(old, f.NewClaimKey); Assert.AreEqual(f.NewClaimKey, f.CurrentClaimKey);
        Assert.HasCount(1, f.Outbox.Messages);
        Assert.AreEqual(old, f.Outbox.Messages[0].Payload.GetType().GetProperty("ClaimKey")!.GetValue(f.Outbox.Messages[0].Payload));
        await f.Claims.Received(1).ReleaseAsync(old, Arg.Any<CancellationToken>());
        await f.Claims.DidNotReceive().ReleaseAsync(f.NewClaimKey, Arg.Any<CancellationToken>());
    }

    private sealed class RecordingOutbox : IOutboxWriter
    {
        internal readonly List<(string Type, int Version, object Payload)> Messages = [];
        internal Func<bool> InTransaction = () => false;
        internal bool Fail;
        public Task AddAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(string eventType,
            int schemaVersion, TEvent payload, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.IsTrue(InTransaction(), "解绑事实必须处于媒体本地事务内。");
            if (Fail) throw new IOException("Outbox unavailable");
            Messages.Add((eventType, schemaVersion, payload!)); return Task.CompletedTask;
        }
        public Task AddAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(string eventType,
            int schemaVersion, TEvent payload, IntegrationEventMetadata metadata, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("普通媒体业务不得覆盖可信信封元数据。");
    }
}
