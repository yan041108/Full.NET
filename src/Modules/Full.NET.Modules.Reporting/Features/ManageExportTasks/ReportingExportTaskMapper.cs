using System.Text.Json;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Serialization;
using Full.NET.Modules.Reporting.Persistence;

namespace Full.NET.Modules.Reporting.Features.ManageExportTasks;

/// <summary>报表导出任务 DTO 映射。</summary>
internal static class ReportingExportTaskMapper
{
    private static readonly JsonSerializerOptions ParameterJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly ReportingJsonSerializerContext SerializerContext = new(ParameterJsonOptions);

    /// <summary>将持久化行映射为列表摘要。</summary>
    public static ReportingExportTaskResponse MapSummary(ReportingExportTaskRecord record) =>
        new(
            record.Id,
            record.DefinitionId,
            record.DefinitionKey,
            record.DefinitionName,
            record.VersionNumber,
            record.FormatKey,
            record.StatusKey,
            record.RowCount,
            record.OutputFileName,
            record.ErrorCode,
            record.ErrorMessage,
            record.RequestedByUserId,
            record.CreatedAtUtc,
            record.CompletedAtUtc);

    /// <summary>将持久化行映射为详情响应。</summary>
    public static ReportingExportTaskDetailResponse MapDetail(ReportingExportTaskRecord record) =>
        new(
            record.Id,
            record.DefinitionId,
            record.DefinitionKey,
            record.DefinitionName,
            record.VersionNumber,
            record.FormatKey,
            record.StatusKey,
            record.RowCount,
            record.OutputFileId,
            record.OutputFileName,
            record.ErrorCode,
            record.ErrorMessage,
            DeserializeParameters(record.ParametersJson),
            record.RequestedByUserId,
            record.CreatedAtUtc,
            record.CompletedAtUtc);

    /// <summary>序列化执行参数为 JSON。</summary>
    /// <param name="parameters">按协议传递的参数集合。</param>
    public static string SerializeParameters(IReadOnlyList<ReportingExecutionParameterValue> parameters) =>
        JsonSerializer.Serialize(parameters, SerializerContext.IReadOnlyListReportingExecutionParameterValue);

    /// <summary>序列化创建导出时的权限码快照。</summary>
    /// <param name="permissionCodes">主体权限码。</param>
    public static string SerializePermissionCodes(IReadOnlyList<string> permissionCodes) =>
        JsonSerializer.Serialize(permissionCodes.ToArray(), SerializerContext.StringArray);

    /// <summary>序列化服务器冻结的原始权限与会话委托，不改变现有数据库列。</summary>
    public static string SerializeAuthorization(string[] codes, Full.NET.Modules.Identity.Contracts.SessionBindingSnapshot binding) =>
        JsonSerializer.Serialize(new ReportingExportAuthorizationSnapshot(codes, binding), SerializerContext.ReportingExportAuthorizationSnapshot);

    /// <summary>严格识别旧数组和新委托；损坏快照失败关闭，旧数组仅供下载原列边界复核。</summary>
    public static ReportingExportAuthorizationSnapshot? DeserializeAuthorization(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            ReportingExportAuthorizationSnapshot? snapshot;
            if (document.RootElement.ValueKind == JsonValueKind.Array)
                snapshot = new(JsonSerializer.Deserialize(json, SerializerContext.StringArray)!, null);
            else if (document.RootElement.ValueKind == JsonValueKind.Object)
                snapshot = JsonSerializer.Deserialize(json, SerializerContext.ReportingExportAuthorizationSnapshot);
            else return null;
            return snapshot?.PermissionCodes is null || snapshot.PermissionCodes.Any(string.IsNullOrWhiteSpace) ? null : snapshot;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>重建原列权限；损坏快照返回空集合，执行器必须先完成独立授权复核。</summary>
    public static IReadOnlyList<string> DeserializePermissionCodes(string? json) =>
        DeserializeAuthorization(json)?.PermissionCodes ?? [];

    /// <summary>从任务快照还原报表参数，保持既有空值语义。</summary>
    /// <param name="json">持久化的 JSON 快照。</param>
    public static IReadOnlyList<ReportingExecutionParameterValue> DeserializeParameters(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize(json, SerializerContext.IReadOnlyListReportingExecutionParameterValue) ?? [];
    }
}
