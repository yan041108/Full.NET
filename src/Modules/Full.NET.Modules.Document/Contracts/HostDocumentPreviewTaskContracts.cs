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
    /// <summary>持久化引用意图已建立，等待源 Claim。</summary>
    public const string Preparing = "preparing";
    /// <summary>已取消；旧领取版本不能再提交。</summary>
    public const string Cancelled = "cancelled";


    /// <summary>已发布的全部状态键。</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        Pending,
        Processing,
        Succeeded,
        Failed,
        Preparing,
        Cancelled,
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
    /// <summary>允许取消预览任务，仍须文档写权限。</summary>
    public const string Cancel = "document.host_preview_tasks.cancel";
    /// <summary>允许新建人工重试任务，仍须文档读取权限。</summary>
    public const string Retry = "document.host_preview_tasks.retry";
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
/// <param name="VersionId">创建时固定的实际版本标识；不会随当前版本变化。</param>
/// <param name="SourceFileId">源文件标识。</param>
/// <param name="OutputFileId">输出 PDF 文件标识；转换未完成或失败时为 <see langword="null"/>。</param>
/// <param name="StatusKey">任务状态键，取值见 <see cref="HostDocumentPreviewTaskStatusKeys"/>。</param>
/// <param name="ProviderKey">转换 Provider 键，取值见 <see cref="DocumentOfficePreviewConversionProviderKeys"/>。</param>
/// <param name="ErrorCode">失败时的稳定错误码；成功或进行中为 <see langword="null"/>。</param>
/// <param name="RequestedByUserId">发起任务的用户标识；匿名分享预览为空。</param>
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
    Guid? RequestedByUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    long Version)
{
    /// <summary>仅供本次匿名分享任务状态与输出读取的短期凭据。</summary>
    public string? PreviewGrant { get; init; }
    /// <summary>实际领取次数，包括崩溃重领。</summary>
    public int AttemptCount { get; init; }
    /// <summary>瞬时失败退避后的下次领取时间。</summary>
    public DateTimeOffset? NextAttemptAtUtc { get; init; }
    /// <summary>服务端允许客户端等待的截止时间；超出后用户可显式重试。</summary>
    public DateTimeOffset? PreviewPollUntilUtc { get; init; }
}

/// <summary>服务端当前转换能力，不包含供应商密钥或本地路径。</summary>
/// <param name="Enabled">当前是否允许提交新转换。</param>
/// <param name="ProviderKey">稳定供应商键。</param>
/// <param name="AllowedMimeTypes">允许转换的 MIME 白名单。</param>
/// <param name="MaxInputBytes">输入字节上限。</param>
/// <param name="MaxAttempts">自动领取次数上限。</param>
public sealed record DocumentPreviewCapabilities(bool Enabled, string ProviderKey, IReadOnlyList<string> AllowedMimeTypes, long MaxInputBytes, int MaxAttempts);
/// <summary>任务取消的并发前置条件。</summary>
/// <param name="Version">页面读取的任务版本。</param>
public sealed record CancelDocumentPreviewTaskRequest(long Version);
