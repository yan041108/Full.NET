using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Features;

/// <summary>将 Host 文档明细行投影为 API 响应，避免查询与管理服务重复映射逻辑。</summary>
internal static class HostDocumentItemResponseMapper
{
    internal static HostDocumentItemResponse Map(
        DocumentItemDetailRecord record,
        IReadOnlyList<HostDocumentTagAssignmentResponse>? tags = null) =>
        new(
            record.Id,
            record.DocumentNo,
            record.Title,
            record.Description,
            record.CategoryId,
            record.CategoryName,
            record.CategoryColor,
            (HostDocumentType)record.DocumentType,
            record.SizeKb,
            record.Thumbnail,
            (HostDocumentStatus)record.Status,
            record.AccessCount,
            record.Sort,
            record.LastAccessTime,
            MapVersion(record),
            tags ?? Array.Empty<HostDocumentTagAssignmentResponse>(),
            record.CreatedAtUtc,
            record.CreatedByUserId,
            record.UpdatedAtUtc,
            record.UpdatedByUserId,
            record.DeletedAtUtc,
            record.DeletedByUserId,
            record.Version);

    private static HostDocumentVersionResponse? MapVersion(DocumentItemDetailRecord record) =>
        record.VersionId is null
            ? null
            : new HostDocumentVersionResponse(
                record.VersionId.Value,
                record.VersionNumber!.Value,
                record.FileId!.Value,
                record.ContentHash,
                record.SizeBytes!.Value,
                record.ChangeDescription,
                record.VersionCreatedAtUtc!.Value,
                record.UploadedByUserId!.Value);
}
