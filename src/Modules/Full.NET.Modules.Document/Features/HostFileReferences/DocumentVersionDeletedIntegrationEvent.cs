using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.Contracts;

namespace Full.NET.Modules.Document.Features.HostFileReferences;

/// <summary>文档自有删除事实发布边界；调用方必须持有文档本地命令事务。</summary>
internal static class DocumentVersionDeletedOutbox
{
    internal const string EventType = "fullnet.document.version.deleted";
    internal const int SchemaVersion = 1;

    internal static Task PublishAsync(IOutboxWriter writer, DocumentVersionRecord version, CancellationToken ct)
    {
        // 历史空文件标识没有可释放的引用，不生成无法消费的清理消息。
        if (version.FileId == Guid.Empty) return Task.CompletedTask;
        return writer.AddAsync(EventType, SchemaVersion,
            new DocumentVersionDeletedIntegrationEvent(version.DocumentItemId, version.Id, version.FileId),
            IntegrationEventMetadata.Create(version.DocumentItemId.ToString("D"), "fullnet.document"), ct);
    }
}
