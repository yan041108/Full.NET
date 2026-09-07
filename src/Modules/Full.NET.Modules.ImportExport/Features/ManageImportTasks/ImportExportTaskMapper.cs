using System.Text.Json;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Persistence;
using Full.NET.Modules.ImportExport.Serialization;

namespace Full.NET.Modules.ImportExport.Features.ManageImportTasks;

/// <summary>导入任务执行进度 JSON 文档，携带能力标记与逐行结果。</summary>
internal sealed record ImportExportExecutionStateDocument(
    IReadOnlyDictionary<string, bool>? CapabilityFlags,
    IReadOnlyList<StaticImportRowExecutionResult> Rows);

/// <summary>导入任务持久化记录与 API 响应映射。</summary>
internal static class ImportExportTaskMapper
{
    private static readonly ImportExportJsonSerializerContext RowSerializerContext = new(new JsonSerializerOptions(JsonSerializerDefaults.Web));

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

    /// <summary>使用静态元数据保存导入行预览快照。</summary>
    /// <param name="rows">需保存的逐行结果。</param>
    public static string SerializePreviewRows(IReadOnlyList<StaticImportRowPreviewResult> rows) =>
        JsonSerializer.Serialize(rows, RowSerializerContext.IReadOnlyListStaticImportRowPreviewResult);

    /// <summary>使用静态元数据保存行执行结果。</summary>
    /// <param name="rows">需保存的逐行结果。</param>
    public static string SerializeExecutionRows(IReadOnlyList<StaticImportRowExecutionResult> rows) =>
        JsonSerializer.Serialize(new ImportExportExecutionStateDocument(null, rows), RowSerializerContext.ImportExportExecutionStateDocument);

    /// <summary>序列化包含恢复位置的执行状态文档。</summary>
    /// <param name="document">包含执行进度和结果的状态文档。</param>
    public static string SerializeExecutionState(ImportExportExecutionStateDocument document) =>
        JsonSerializer.Serialize(document, RowSerializerContext.ImportExportExecutionStateDocument);

    /// <summary>读取执行状态并兼容既有行结果快照。</summary>
    /// <param name="json">持久化的 JSON 快照。</param>
    public static ImportExportExecutionStateDocument DeserializeExecutionState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ImportExportExecutionStateDocument(null, []);
        }

        using var parsed = JsonDocument.Parse(json);
        if (parsed.RootElement.ValueKind == JsonValueKind.Array)
        {
            // 旧版本直接保存结果数组，必须在对象反序列化之前分流，避免升级后恢复失败。
            var legacyRows = parsed.RootElement.Deserialize(RowSerializerContext.IReadOnlyListStaticImportRowExecutionResult);
            return new ImportExportExecutionStateDocument(null, legacyRows ?? []);
        }

        return parsed.RootElement.Deserialize(RowSerializerContext.ImportExportExecutionStateDocument)
            ?? new ImportExportExecutionStateDocument(null, []);
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

    /// <summary>读取任务保存的预览行，空快照返回空集合。</summary>
    /// <param name="json">持久化的 JSON 快照。</param>
    private static IReadOnlyList<StaticImportRowPreviewResult> DeserializePreviewRows(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize(
                   json,
                   RowSerializerContext.IReadOnlyListStaticImportRowPreviewResult)
               ?? [];
    }
}
