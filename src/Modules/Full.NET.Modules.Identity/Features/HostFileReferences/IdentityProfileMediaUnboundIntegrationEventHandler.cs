using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;

namespace Full.NET.Modules.Identity.Features.HostFileReferences;

/// <summary>只释放事务事实指定的旧绑定键；Files 自有事务失败必须交给 Worker 重试。</summary>
internal sealed class IdentityProfileMediaUnboundIntegrationEventHandler(
    IIntegrationEventSerializer serializer, IHostFileReferenceClaimService? claims = null) : IIntegrationEventHandler
{
    public string EventType => IdentityProfileMediaUnboundOutbox.EventType;
    public int SchemaVersion => IdentityProfileMediaUnboundOutbox.SchemaVersion;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.NaturallyIdempotent;
    public async Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TenantId is not null || context.MessageId == Guid.Empty || context.SchemaVersion != SchemaVersion
            || !string.Equals(context.MessageType, EventType, StringComparison.Ordinal))
            throw new InvalidDataException("identity.profile_media_unbound.invalid_context");
        var message = serializer.Deserialize<IdentityProfileMediaUnboundIntegrationEvent>(payload);
        if (message is null || message.UserId == Guid.Empty || message.FileId == Guid.Empty)
            throw new InvalidDataException("identity.profile_media_unbound.invalid_payload");
        var legacy = message.MediaKind switch
        {
            "avatar" => HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(message.UserId, message.FileId),
            "signature" => HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(message.UserId, message.FileId),
            _ => throw new InvalidDataException("identity.profile_media_unbound.invalid_kind"),
        };
        if (!HostFileReferenceClaimIdempotencyKeys.IsBindingKey(legacy, message.ClaimKey))
            throw new InvalidDataException("identity.profile_media_unbound.invalid_claim_key");
        if (claims is null) throw new InvalidOperationException("identity.profile_media_unbound.files_unavailable");
        var result = await claims.ReleaseAsync(message.ClaimKey, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.IsSuccess && result.Value) return;
        if (!result.IsSuccess && result.Error!.Code == FilesErrorCodes.ClaimNotFound) return;
        throw new InvalidOperationException("identity.profile_media_unbound.release_failed");
    }
    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidDataException("identity.profile_media_unbound.context_required");
}
