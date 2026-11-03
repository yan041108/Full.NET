using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Serialization;

namespace Full.NET.Modules.ImportExport.Domain;

/// <summary>任务保存独立输入，不保存个人配置指针；配置后续变更不能改变已排队的列含义。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ImportColumnMappingSnapshot([property: JsonRequired] string HeaderFingerprint,
    [property: JsonRequired] ImportColumnMappingRequest Configuration);

/// <summary>无自定义映射时两个值都为空，表示使用原固定模板。</summary>
internal sealed record CapturedImportColumnMapping(ImportColumnMappingRequest? Configuration, string? Json);

/// <summary>创建与执行共享表头兼容性及体积验证，损坏输入不能静默退回标准模板。</summary>
internal static class ImportColumnMappingSnapshots
{
    internal const int MaximumSnapshotBytes = StaticImportHeaderMapping.MaximumConfigurationBytes + 512;

    internal static Result<CapturedImportColumnMapping> Capture(StaticImportSchemaDefinition definition, string worksheetKey, ImportColumnMappingRequest? request)
    {
        if (request is null) return Result<CapturedImportColumnMapping>.Success(new(null, null));
        var worksheet = definition.Worksheets.SingleOrDefault(item => item.WorksheetKey == worksheetKey);
        if (worksheet is null || request.HeaderColumns is null) return Result<CapturedImportColumnMapping>.Failure(Invalid());
        if (!request.HeaderColumns.SequenceEqual(worksheet.HeaderColumns, StringComparer.Ordinal))
            return Result<CapturedImportColumnMapping>.Failure(Changed());
        if (!StaticImportHeaderMapping.TryNormalize(worksheet.HeaderColumns, request, out var normalized))
            return Result<CapturedImportColumnMapping>.Failure(Invalid());
        var snapshot = new ImportColumnMappingSnapshot(StaticImportHeaderMapping.Fingerprint(worksheet.HeaderColumns), normalized);
        var json = JsonSerializer.Serialize(snapshot, ImportExportJsonSerializerContext.Default.ImportColumnMappingSnapshot);
        return Encoding.UTF8.GetByteCount(json) <= MaximumSnapshotBytes
            ? Result<CapturedImportColumnMapping>.Success(new(normalized, json)) : Result<CapturedImportColumnMapping>.Failure(Invalid());
    }

    internal static Result<CapturedImportColumnMapping> Read(StaticImportSchemaDefinition definition, string worksheetKey, string? json)
    {
        if (json is null) return Result<CapturedImportColumnMapping>.Success(new(null, null));
        if (Encoding.UTF8.GetByteCount(json) > MaximumSnapshotBytes) return Result<CapturedImportColumnMapping>.Failure(Invalid());
        try
        {
            var snapshot = JsonSerializer.Deserialize(json, ImportExportJsonSerializerContext.Default.ImportColumnMappingSnapshot);
            var worksheet = definition.Worksheets.SingleOrDefault(item => item.WorksheetKey == worksheetKey);
            if (snapshot?.Configuration is null || worksheet is null) return Result<CapturedImportColumnMapping>.Failure(Invalid());
            if (!string.Equals(snapshot.HeaderFingerprint, StaticImportHeaderMapping.Fingerprint(worksheet.HeaderColumns), StringComparison.Ordinal))
                return Result<CapturedImportColumnMapping>.Failure(Changed());
            return Capture(definition, worksheetKey, snapshot.Configuration);
        }
        catch (JsonException) { return Result<CapturedImportColumnMapping>.Failure(Invalid()); }
    }

    /// <summary>详情只取有界展示列，不输出指纹封装或原始 JSON；执行仍使用上面的完整复核。</summary>
    internal static IReadOnlyList<ImportColumnMapping>? Display(string? json)
    {
        if (json is null || Encoding.UTF8.GetByteCount(json) > MaximumSnapshotBytes) return null;
        try
        {
            var snapshot = JsonSerializer.Deserialize(json, ImportExportJsonSerializerContext.Default.ImportColumnMappingSnapshot);
            return snapshot?.Configuration?.HeaderColumns is { } headers
                && StaticImportHeaderMapping.TryNormalize(headers, snapshot.Configuration, out var normalized) ? normalized.Columns : null;
        }
        catch (JsonException) { return null; }
    }

    internal static Error Invalid() => new(ImportColumnMappingErrorCodes.Invalid, "The column mapping or workbook headers are invalid.", ErrorType.Validation);
    private static Error Changed() => new(ImportColumnMappingErrorCodes.SchemaChanged, "The source headers have changed. Create a new import with the current template.", ErrorType.Conflict);
}
