using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.ImportExport.Contracts;

/// <summary>ImportExport 模块稳定权限码。</summary>
public static class ImportExportPermissions
{
    /// <summary>允许读取已注册的静态导入 Schema 目录。</summary>
    public const string StaticSchemasRead = "import_export.static_schemas.read";

    /// <summary>允许分页读取导入任务。</summary>
    public const string ImportTasksRead = "import_export.import_tasks.read";

    /// <summary>允许创建导入任务并执行预校验。</summary>
    public const string ImportTasksCreate = "import_export.import_tasks.create";

    /// <summary>允许将预校验成功的导入任务排队执行、恢复或重试。</summary>
    public const string ImportTasksExecute = "import_export.import_tasks.execute";
}

/// <summary>内置静态导入 Schema 稳定键。</summary>
public static class StaticImportSchemaKeys
{
    /// <summary>Organization 租户职位固定模板导入。</summary>
    public const string OrganizationTenantPositions = "organization.tenant_positions";

    /// <summary>Enterprise Request 样板单据 CSV 导入。</summary>
    public const string DemoEnterpriseRequests = "demo.enterprise_requests";
}

/// <summary>静态导入 Schema 授权作用域。</summary>
public static class StaticImportSchemaScopeKeys
{
    /// <summary>租户作用域 Schema，必须绑定当前租户上下文。</summary>
    public const string Tenant = "tenant";
}

/// <summary>导入任务状态稳定键。</summary>
public static class ImportExportTaskStatusKeys
{
    /// <summary>文件已上传，等待预校验。</summary>
    public const string Uploaded = "uploaded";

    /// <summary>预校验成功，可进入后续执行阶段。</summary>
    public const string PreviewSucceeded = "preview_succeeded";

    /// <summary>预校验失败，可查看行级错误。</summary>
    public const string PreviewFailed = "preview_failed";

    /// <summary>已排队等待后台批量执行。</summary>
    public const string Queued = "queued";

    /// <summary>后台 worker 正在批量写入。</summary>
    public const string Executing = "executing";

    /// <summary>全部有效行执行成功。</summary>
    public const string ExecutionSucceeded = "execution_succeeded";

    /// <summary>部分有效行执行成功，存在失败行。</summary>
    public const string ExecutionPartial = "execution_partial";

    /// <summary>执行阶段失败，无成功行或任务级失败。</summary>
    public const string ExecutionFailed = "execution_failed";

    /// <summary>已发布的全部状态键。</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        Uploaded,
        PreviewSucceeded,
        PreviewFailed,
        Queued,
        Executing,
        ExecutionSucceeded,
        ExecutionPartial,
        ExecutionFailed,
    ]);
}

/// <summary>静态导入工作表定义。</summary>
public sealed record StaticImportWorksheetDefinition(
    string WorksheetKey,
    string DisplayName,
    IReadOnlyList<string> HeaderColumns);

/// <summary>静态导入 Schema 元数据。</summary>
public sealed record StaticImportSchemaDefinition(
    string SchemaKey,
    string DisplayName,
    string ScopeKey,
    string RequiredPermission,
    IReadOnlyList<StaticImportWorksheetDefinition> Worksheets);

/// <summary>静态导入预校验上下文。</summary>
public sealed record StaticImportPreviewContext(
    Guid RequestedByUserId,
    IReadOnlyDictionary<string, bool> CapabilityFlags)
{
    /// <summary>执行时由调度方提供的持久化任务标识；预览时为空，处理器以它和原始行号建立幂等。</summary>
    public Guid? TaskId { get; init; }
}

/// <summary>单行预校验结果。</summary>
public sealed record StaticImportRowPreviewResult(
    int LineNumber,
    bool IsValid,
    string? ErrorCode,
    string? Message);

/// <summary>静态导入预校验汇总。</summary>
public sealed record StaticImportPreviewResult(
    int TotalRows,
    int ValidRowCount,
    int InvalidRowCount,
    IReadOnlyList<StaticImportRowPreviewResult> Rows);

/// <summary>单行批量执行结果。</summary>
/// <param name="LineNumber">原始工作簿行号（从 1 开始）。</param>
/// <param name="Succeeded">本行是否写入成功。</param>
/// <param name="EntityId">成功时返回业务实体标识。</param>
/// <param name="ErrorCode">失败时返回稳定错误码。</param>
/// <param name="Message">失败时的可读说明。</param>
public sealed record StaticImportRowExecutionResult(
    int LineNumber,
    bool Succeeded,
    Guid? EntityId,
    string? ErrorCode,
    string? Message);

/// <summary>静态导入批量执行汇总。</summary>
/// <param name="Rows">本批逐行执行结果。</param>
public sealed record StaticImportBatchExecutionResult(
    IReadOnlyList<StaticImportRowExecutionResult> Rows);

/// <summary>消费方模块实现的静态 Schema 处理器。</summary>
public interface IStaticImportSchemaHandler
{
    /// <summary>处理器负责的 Schema 稳定键。</summary>
    string SchemaKey { get; }

    /// <summary>返回 Schema 元数据，供目录与模板端点投影。</summary>
    StaticImportSchemaDefinition GetDefinition();

    /// <summary>生成指定工作表的导入模板字节流。</summary>
    byte[] CreateTemplate(string worksheetKey);

    /// <summary>解析并预校验上传内容，不得产生跨模块写入副作用。</summary>
    Task<Result<StaticImportPreviewResult>> PreviewAsync(
        Stream content,
        long contentLength,
        StaticImportPreviewContext context,
        CancellationToken cancellationToken = default);

    /// <summary>按有效行检查点批量执行导入写入；调用方模块负责实际持久化。</summary>
    /// <param name="content">源工作簿只读流。</param>
    /// <param name="contentLength">声明内容长度。</param>
    /// <param name="startLineNumber">有效行序号检查点（从 0 开始）。</param>
    /// <param name="batchSize">本批最多处理的 valid 行数。</param>
    /// <param name="context">执行上下文，含请求用户与能力标记。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result<StaticImportBatchExecutionResult>> ExecuteBatchAsync(
        Stream content,
        long contentLength,
        int startLineNumber,
        int batchSize,
        StaticImportPreviewContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>导入任务列表项响应。</summary>
public sealed record ImportExportTaskResponse(
    Guid Id,
    Guid TenantId,
    string SchemaKey,
    string SchemaDisplayName,
    string WorksheetKey,
    Guid SourceFileId,
    string? SourceFileName,
    string StatusKey,
    int TotalRows,
    int ValidRowCount,
    int InvalidRowCount,
    string? ErrorCode,
    Guid RequestedByUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PreviewCompletedAtUtc,
    int ProcessedRowCount,
    int SucceededRowCount,
    int ExecutionFailedRowCount,
    int NextLineNumber,
    DateTimeOffset? ExecutionStartedAtUtc,
    DateTimeOffset? ExecutionCompletedAtUtc,
    bool HasErrorReceipt,
    long Version);

/// <summary>导入任务详情响应，包含行级预校验结果。</summary>
public sealed record ImportExportTaskDetailResponse(
    Guid Id,
    Guid TenantId,
    string SchemaKey,
    string SchemaDisplayName,
    string WorksheetKey,
    Guid SourceFileId,
    string? SourceFileName,
    string StatusKey,
    int TotalRows,
    int ValidRowCount,
    int InvalidRowCount,
    string? ErrorCode,
    Guid RequestedByUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PreviewCompletedAtUtc,
    int ProcessedRowCount,
    int SucceededRowCount,
    int ExecutionFailedRowCount,
    int NextLineNumber,
    DateTimeOffset? ExecutionStartedAtUtc,
    DateTimeOffset? ExecutionCompletedAtUtc,
    bool HasErrorReceipt,
    IReadOnlyList<StaticImportRowPreviewResult> PreviewRows,
    long Version);
