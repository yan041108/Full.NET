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

    /// <summary>还原权限码快照；损坏时返回空集合，恢复路径会按无列权限失败关闭。</summary>
    /// <param name="json">持久化 JSON。</param>
    public static IReadOnlyList<string> DeserializePermissionCodes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize(json, SerializerContext.StringArray) ?? [];
    }

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
