using System.Text.Json;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Persistence;

namespace Full.NET.Modules.ImportExport.Features.ManageImportTasks;

/// <summary>导入任务执行进度 JSON 文档，携带能力标记与逐行结果。</summary>
internal sealed record ImportExportExecutionStateDocument(
    IReadOnlyDictionary<string, bool>? CapabilityFlags,
    IReadOnlyList<StaticImportRowExecutionResult> Rows);

/// <summary>导入任务持久化记录与 API 响应映射。</summary>
internal static class ImportExportTaskMapper
{
    private static readonly JsonSerializerOptions RowSerializerOptions = new(JsonSerializerDefaults.Web);

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
            record.ProcessedRowCount,
            record.SucceededRowCount,
            record.ExecutionFailedRowCount,
            record.NextLineNumber,
            record.ExecutionStartedAtUtc,
            record.ExecutionCompletedAtUtc,
            record.ErrorReceiptFileId is not null,
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
            record.ProcessedRowCount,
            record.SucceededRowCount,
            record.ExecutionFailedRowCount,
            record.NextLineNumber,
            record.ExecutionStartedAtUtc,
            record.ExecutionCompletedAtUtc,
            record.ErrorReceiptFileId is not null,
            DeserializePreviewRows(record.PreviewRowsJson),
            record.Version);

    public static string SerializePreviewRows(IReadOnlyList<StaticImportRowPreviewResult> rows) =>
        JsonSerializer.Serialize(rows, RowSerializerOptions);

    public static string SerializeExecutionRows(IReadOnlyList<StaticImportRowExecutionResult> rows) =>
        JsonSerializer.Serialize(new ImportExportExecutionStateDocument(null, rows), RowSerializerOptions);

    public static string SerializeExecutionState(ImportExportExecutionStateDocument document) =>
        JsonSerializer.Serialize(document, RowSerializerOptions);

    public static ImportExportExecutionStateDocument DeserializeExecutionState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ImportExportExecutionStateDocument(null, []);
        }

        var document = JsonSerializer.Deserialize<ImportExportExecutionStateDocument>(json, RowSerializerOptions);
        if (document is not null)
        {
            return document;
        }

        var legacyRows = JsonSerializer.Deserialize<IReadOnlyList<StaticImportRowExecutionResult>>(
            json,
            RowSerializerOptions);
        return new ImportExportExecutionStateDocument(null, legacyRows ?? []);
    }

    public static IReadOnlyList<StaticImportRowExecutionResult> DeserializeExecutionRows(string? json) =>
        DeserializeExecutionState(json).Rows;

    public static IReadOnlyList<StaticImportRowExecutionResult> MergeExecutionRows(
        IReadOnlyList<StaticImportRowExecutionResult> existing,
        IReadOnlyList<StaticImportRowExecutionResult> batch)
    {
        if (existing.Count == 0)
        {
            return batch.ToArray();
        }

        var merged = new List<StaticImportRowExecutionResult>(existing.Count + batch.Count);
        merged.AddRange(existing);
        merged.AddRange(batch);
        return merged;
    }

    private static IReadOnlyList<StaticImportRowPreviewResult> DeserializePreviewRows(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<IReadOnlyList<StaticImportRowPreviewResult>>(
                   json,
                   RowSerializerOptions)
               ?? [];
    }
}
