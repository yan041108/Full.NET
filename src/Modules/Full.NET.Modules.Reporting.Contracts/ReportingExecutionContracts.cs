namespace Full.NET.Modules.Reporting.Contracts;

/// <summary>报表执行权限码。</summary>
public static class ReportingExecutionPermissions
{
    /// <summary>执行已发布报表定义并读取分页结果。</summary>
    public const string Run = "reporting.executions.run";

    /// <summary>读取 Schema 清单结果中的 SchemaName 列。</summary>
    public const string ColumnSchemaName = "reporting.executions.columns.schema_name";
}

/// <summary>报表执行参数值。</summary>
/// <param name="ParameterKey">参数键。</param>
/// <param name="Value">参数文本值。</param>
public sealed record ReportingExecutionParameterValue(
    string ParameterKey,
    string? Value);

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>执行已发布报表定义请求。</summary>
/// <param name="VersionNumber">目标发布版本号；省略时使用最近发布版本。</param>
/// <param name="Parameters">受控参数值集合。</param>
public sealed record ExecuteReportingDefinitionRequest(
    int? VersionNumber,
    IReadOnlyList<ReportingExecutionParameterValue> Parameters);

/// <summary>报表执行结果列定义。</summary>
/// <param name="ColumnKey">列键。</param>
/// <param name="DisplayName">显示名称。</param>
public sealed record ReportingExecutionColumnDefinition(
    string ColumnKey,
    string DisplayName);

/// <summary>报表执行结果行。</summary>
/// <param name="Values">按列键索引的单元格文本值。</param>
public sealed record ReportingExecutionRow(
    IReadOnlyDictionary<string, string?> Values);

/// <summary>报表执行分页结果。</summary>
/// <param name="DefinitionId">定义标识。</param>
/// <param name="DefinitionKey">稳定定义键。</param>
/// <param name="DefinitionName">显示名称。</param>
/// <param name="VersionNumber">执行的发布版本号。</param>
/// <param name="QueryPortKey">静态 Query Port 键。</param>
/// <param name="Columns">经列权限过滤后的可见列。</param>
/// <param name="Rows">当前页数据行。</param>
/// <param name="Page">页码，从 1 开始。</param>
/// <param name="PageSize">每页条数。</param>
/// <param name="HasMore">是否仍有下一页。</param>
/// <param name="TotalRows">受 topN 等上限约束后的总行数估计；未知时为 <see langword="null"/>。</param>
/// <param name="CommandTimeoutSeconds">本次执行使用的命令超时秒数。</param>
/// <param name="ExecutedAtUtc">执行完成时间（UTC）。</param>
public sealed record ReportingExecutionPageResponse(
    Guid DefinitionId,
    string DefinitionKey,
    string DefinitionName,
    int VersionNumber,
    string QueryPortKey,
    IReadOnlyList<ReportingExecutionColumnDefinition> Columns,
    IReadOnlyList<ReportingExecutionRow> Rows,
    int Page,
    int PageSize,
    bool HasMore,
    int? TotalRows,
    int CommandTimeoutSeconds,
    DateTimeOffset ExecutedAtUtc);
