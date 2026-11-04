using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;

namespace Full.NET.Modules.Tenancy.Features.HostFileReferences;

/// <summary>可信 Host/Tenant 解绑事实仅释放对应 Logo 代际，临时 Host 调用始终恢复作用域。</summary>
internal sealed class TenantLogoUnboundIntegrationEventHandler(
    IIntegrationEventSerializer serializer, ICurrentTenantContextWriter tenantWriter,
    IHostFileReferenceClaimService? claims = null) : IIntegrationEventHandler
{
    public string EventType => TenantLogoUnboundOutbox.EventType;
    public int SchemaVersion => TenantLogoUnboundOutbox.SchemaVersion;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.NaturallyIdempotent;
    public async Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.MessageId == Guid.Empty || context.SchemaVersion != SchemaVersion
            || !string.Equals(context.MessageType, EventType, StringComparison.Ordinal))
            throw new InvalidDataException("tenancy.logo_unbound.invalid_context");
        var message = serializer.Deserialize<TenantLogoUnboundIntegrationEvent>(payload);
        if (message is null || message.TenantId == Guid.Empty || message.FileId == Guid.Empty
            || context.TenantId is Guid tenant && tenant != message.TenantId)
            throw new InvalidDataException("tenancy.logo_unbound.invalid_payload");
        var legacy = HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(message.TenantId, message.FileId);
        if (!HostFileReferenceClaimIdempotencyKeys.IsBindingKey(legacy, message.ClaimKey))
            throw new InvalidDataException("tenancy.logo_unbound.invalid_claim_key");
        if (claims is null) throw new InvalidOperationException("tenancy.logo_unbound.files_unavailable");
        var result = await TenancyHostExecutionScope.RunAsync(tenantWriter,
            () => claims.ReleaseAsync(message.ClaimKey, cancellationToken)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.IsSuccess && result.Value) return;
        if (!result.IsSuccess && result.Error!.Code == FilesErrorCodes.ClaimNotFound) return;
        throw new InvalidOperationException("tenancy.logo_unbound.release_failed");
    }
    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidDataException("tenancy.logo_unbound.context_required");
}
