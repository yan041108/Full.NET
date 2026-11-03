using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;

namespace Full.NET.Modules.Document.Features;

/// <summary>删除本地引用与可靠释放事实同事务提交，提交后宕机仍可重放。</summary>
internal sealed class DocumentReferenceReleasePublisher(IOutboxWriter outbox)
{
    internal Task PublishAsync(Guid referenceId, Guid fileId, CancellationToken cancellationToken, bool isPreview = false, DateTimeOffset? notBeforeUtc = null) =>
        outbox.AddAsync(DocumentFileReferenceEventTypes.Released, 1,
            new DocumentFileReferenceReleased(referenceId, fileId, isPreview, notBeforeUtc), cancellationToken);
}
