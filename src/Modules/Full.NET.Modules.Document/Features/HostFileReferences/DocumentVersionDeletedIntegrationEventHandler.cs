using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Document.Contracts;

namespace Full.NET.Modules.Document.Features.HostFileReferences;

/// <summary>处理已提交的文档删除事实；释放事务归 Files 端口所有，失败必须向 Worker 传播。</summary>
internal sealed class DocumentVersionDeletedIntegrationEventHandler(
    IIntegrationEventSerializer serializer,
    IHostFileReferenceClaimService claims) : IIntegrationEventHandler
{
    public string EventType => DocumentVersionDeletedOutbox.EventType;
    public int SchemaVersion => DocumentVersionDeletedOutbox.SchemaVersion;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

    public async Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Host 文件事实不能被租户信封借用；载荷不能覆盖由 Worker 恢复的受信作用域。
        if (context.TenantId is not null || context.SchemaVersion != SchemaVersion
            || !string.Equals(context.MessageType, EventType, StringComparison.Ordinal) || context.MessageId == Guid.Empty)
            throw new InvalidDataException("document.version_deleted.invalid_context");
        var message = serializer.Deserialize<DocumentVersionDeletedIntegrationEvent>(payload);
        if (message is null || message.DocumentItemId == Guid.Empty || message.VersionId == Guid.Empty || message.FileId == Guid.Empty)
            throw new InvalidDataException("document.version_deleted.invalid_payload");
        var result = await claims.ReleaseAsync(HostFileReferenceClaimIdempotencyKeys.DocumentVersion(message.VersionId),
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.IsSuccess && result.Value) return;
        // 旧版本可能从未建立 Claim，或释放行已被保留策略清理；不存在的引用不需要重试。
        if (!result.IsSuccess && string.Equals(result.Error!.Code, FilesErrorCodes.ClaimNotFound, StringComparison.Ordinal)) return;
        throw new InvalidOperationException("document.file_reference_claim.release_failed");
    }

    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidDataException("document.version_deleted.context_required");
}
