using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Persistence;

namespace Full.NET.Modules.Files.Features.HostFileReferenceClaims;

/// <summary>释放已删除版本的 Claim；重复交付只重放同一幂等释放键。</summary>
internal sealed class DocumentFileReferenceReleasedHandler(
    IIntegrationEventSerializer serializer, IHostFileReferenceClaimService claims, IQueryExecutor queries, IClock clock) : IIntegrationEventHandler
{
    public string EventType => DocumentFileReferenceEventTypes.Released;
    public int SchemaVersion => 1;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

    public async Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var message = serializer.Deserialize<DocumentFileReferenceReleased>(payload);
        if (message.ReferenceId == Guid.Empty || message.FileId == Guid.Empty)
            throw new InvalidOperationException("Invalid document reference release.");
        // 延期释放由 Document 持久化意图到期后再次发布；提前交付只确认收悉，不占用重试预算。
        if (message.NotBeforeUtc > clock.UtcNow) return;
        var key = message.IsPreview ? HostFileReferenceClaimIdempotencyKeys.DocumentPreview(message.ReferenceId)
            : HostFileReferenceClaimIdempotencyKeys.DocumentVersion(message.ReferenceId);
        var claim = await queries.QuerySingleOrDefaultAsync<HostFileReferenceClaimRecord>(HostFileReferenceClaimSql.FindByIdempotencyKey,
            new Dictionary<string, object?> { ["IdempotencyKey"] = key }, cancellationToken).ConfigureAwait(false);
        if (claim is null) return;
        if (claim.ConsumerModule != HostFileReferenceClaimConsumerModules.Document || claim.ConsumerReferenceId != message.ReferenceId || claim.FileId != message.FileId)
            throw new InvalidOperationException("Document reference release identity mismatch.");
        var result = await claims.ReleaseAsync(key, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess) throw new InvalidOperationException(result.Error!.Code);
    }
}
