using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;

namespace Full.NET.Modules.Files.Features.HostFileReferenceClaims;

/// <summary>内部引用释放在 Files 自己的事务内执行；失败保留 Outbox 消息供重试，不能吞掉 Result。</summary>
internal sealed class HostFileReferenceReleaseRequestedHandler(IIntegrationEventSerializer serializer,
    IHostFileReferenceClaimService claims, ICurrentTenantContextWriter tenant) : IIntegrationEventHandler
{
    public string EventType => HostFileReferenceReleaseRequested.EventType;
    public int SchemaVersion => 1;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.NaturallyIdempotent;
    public async Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var request = serializer.Deserialize<HostFileReferenceReleaseRequested>(payload);
        var wasHost = tenant.IsHost;
        var previous = tenant.Id is { } id ? new TenantContext(id, tenant.Identifier!, tenant.Name!) : null;
        tenant.SetHost();
        try
        {
            var result = await claims.ReleaseAsync(request.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess) throw new InvalidOperationException("files.reference_release_failed");
        }
        finally { if (previous is not null) tenant.SetTenant(previous); else if (wasHost) tenant.SetHost(); else tenant.Clear(); }
    }
}
