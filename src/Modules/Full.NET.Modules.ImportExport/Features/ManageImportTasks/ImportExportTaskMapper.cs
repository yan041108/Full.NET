using System.Text.Json;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Persistence;

namespace Full.NET.Modules.ImportExport.Features.ManageImportTasks;

/// <summary>导入任务持久化记录与 API 响应映射。</summary>
internal static class ImportExportTaskMapper
{
    private static readonly JsonSerializerOptions PreviewRowSerializerOptions = new(JsonSerializerDefaults.Web);

    public static ImportExportTaskResponse MapSummary(ImportExportTaskRecord record) =>
        new(
            record.Id,
            record.TenantId,
            record.SchemaKey,
            record.SchemaDisplayName,
            record.WorksheetKey,
            record.SourceFileId,
            record.SourceFileName,
            record.StatusKey,
            record.TotalRows,
            record.ValidRowCount,
            record.InvalidRowCount,
            record.ErrorCode,
            record.RequestedByUserId,
            record.CreatedAtUtc,
            record.PreviewCompletedAtUtc,
            record.Version);

    public static ImportExportTaskDetailResponse MapDetail(ImportExportTaskRecord record) =>
        new(
            record.Id,
            record.TenantId,
            record.SchemaKey,
            record.SchemaDisplayName,
            record.WorksheetKey,
            record.SourceFileId,
            record.SourceFileName,
            record.StatusKey,
            record.TotalRows,
            record.ValidRowCount,
            record.InvalidRowCount,
            record.ErrorCode,
            record.RequestedByUserId,
            record.CreatedAtUtc,
            record.PreviewCompletedAtUtc,
            DeserializePreviewRows(record.PreviewRowsJson),
            record.Version);

    public static string SerializePreviewRows(IReadOnlyList<StaticImportRowPreviewResult> rows) =>
        JsonSerializer.Serialize(rows, PreviewRowSerializerOptions);

    private static IReadOnlyList<StaticImportRowPreviewResult> DeserializePreviewRows(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<IReadOnlyList<StaticImportRowPreviewResult>>(
                   json,
                   PreviewRowSerializerOptions)
               ?? [];
    }
}
