using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentPreviewTasks;

/// <summary>将预览任务持久化记录映射为公开响应契约。</summary>
internal static class HostDocumentPreviewTaskMapper
{
    public static HostDocumentPreviewTaskResponse Map(DocumentPreviewTaskRecord record) =>
        new(
            record.Id,
            record.DocumentItemId,
            record.DocumentTitle,
            record.VersionId,
            record.SourceFileId,
            record.OutputFileId,
            record.StatusKey,
            record.ProviderKey,
            record.ErrorCode,
            record.RequestedByUserId,
            record.CreatedAtUtc,
            record.StartedAtUtc,
            record.CompletedAtUtc,
            record.Version);
}
