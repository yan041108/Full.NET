namespace Full.NET.Modules.Document.Contracts;

/// <summary>文档 Office 预览转换任务状态稳定键。</summary>
/// <remarks>状态键字符串发布后不可改名或删除；新增状态只能追加，已发布状态键不得调整顺序。</remarks>
public static class HostDocumentPreviewTaskStatusKeys
{
    /// <summary>已入队，等待 Worker 领取。</summary>
    public const string Pending = "pending";

    /// <summary>Worker 正在执行转换。</summary>
    public const string Processing = "processing";

    /// <summary>转换成功且输出 PDF 已写入 Files。</summary>
    public const string Succeeded = "succeeded";

    /// <summary>转换失败，可查看错误码。</summary>
    public const string Failed = "failed";

    /// <summary>已发布的全部状态键。</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        Pending,
        Processing,
        Succeeded,
        Failed,
    ]);
}

/// <summary>Office→PDF 转换 Provider 稳定键。</summary>
/// <remarks>Provider 键字符串发布后不可改名或删除；新增 Provider 只能追加，已发布 Provider 键不得调整顺序。</remarks>
public static class DocumentOfficePreviewConversionProviderKeys
{
    /// <summary>通过受控 HTTP 网关调用外部已授权转换服务。</summary>
    public const string ExternalHttp = "external_http";

    /// <summary>通过隔离子进程调用运维配置的可执行转换器。</summary>
    public const string ExternalProcess = "external_process";

    /// <summary>显式禁用转换，任务将快速失败并提示未配置。</summary>
    public const string Disabled = "disabled";
}

/// <summary>Host 文档预览任务稳定权限码。</summary>
/// <remarks>权限码字符串发布后不可改名或删除；新增权限只能追加，已发布权限码不得调整顺序。</remarks>
public static class HostDocumentPreviewTaskPermissions
{
    /// <summary>允许分页读取预览转换任务。</summary>
    public const string Read = "document.host_preview_tasks.read";

    /// <summary>允许为 Office 文档创建预览转换任务。</summary>
    public const string Create = "document.host_preview_tasks.create";
}

/// <summary>创建 Host 文档预览转换任务请求。</summary>
/// <param name="DocumentItemId">目标文档项标识。</param>
/// <param name="VersionId">可选历史版本标识；为空表示当前版本。</param>
public sealed record CreateHostDocumentPreviewTaskRequest(
    Guid DocumentItemId,
    Guid? VersionId);

/// <summary>单条 Host 文档预览转换任务响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">预览转换任务唯一标识。</param>
/// <param name="DocumentItemId">目标文档项标识。</param>
/// <param name="DocumentTitle">文档标题快照。</param>
/// <param name="VersionId">转换的历史版本标识；为空表示当前版本。</param>
/// <param name="SourceFileId">源文件标识。</param>
/// <param name="OutputFileId">输出 PDF 文件标识；转换未完成或失败时为 <see langword="null"/>。</param>
/// <param name="StatusKey">任务状态键，取值见 <see cref="HostDocumentPreviewTaskStatusKeys"/>。</param>
/// <param name="ProviderKey">转换 Provider 键，取值见 <see cref="DocumentOfficePreviewConversionProviderKeys"/>。</param>
/// <param name="ErrorCode">失败时的稳定错误码；成功或进行中为 <see langword="null"/>。</param>
/// <param name="RequestedByUserId">发起任务的用户标识。</param>
/// <param name="CreatedAtUtc">任务创建时间（UTC）。</param>
/// <param name="StartedAtUtc">Worker 开始转换时间（UTC）；未开始为 <see langword="null"/>。</param>
/// <param name="CompletedAtUtc">转换完成时间（UTC）；未完成为 <see langword="null"/>。</param>
/// <param name="Version">任务记录乐观并发版本号。</param>
public sealed record HostDocumentPreviewTaskResponse(
    Guid Id,
    Guid DocumentItemId,
    string DocumentTitle,
    Guid? VersionId,
    Guid SourceFileId,
    Guid? OutputFileId,
    string StatusKey,
    string ProviderKey,
    string? ErrorCode,
    Guid RequestedByUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    long Version);
