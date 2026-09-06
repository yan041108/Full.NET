using System.Text.Json;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Persistence;

namespace Full.NET.Modules.Reporting.Features.ManageExportTasks;

/// <summary>报表导出任务 DTO 映射。</summary>
internal static class ReportingExportTaskMapper
{
    private static readonly JsonSerializerOptions ParameterJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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
    public static string SerializeParameters(IReadOnlyList<ReportingExecutionParameterValue> parameters) =>
        JsonSerializer.Serialize(parameters, ParameterJsonOptions);

    private static IReadOnlyList<ReportingExecutionParameterValue> DeserializeParameters(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<ReportingExecutionParameterValue[]>(json, ParameterJsonOptions) ?? [];
    }
}
