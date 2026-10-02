using System.Text.Json.Serialization;

namespace Full.NET.Modules.Document.Contracts;

/// <summary>文档访问类型稳定键。</summary>
public static class HostDocumentAccessTypeKeys
{
    /// <summary>下载当前或指定版本文件内容。</summary>
    public const string Download = "download";

    /// <summary>内联预览文档内容。</summary>
    public const string Preview = "preview";

    /// <summary>通过匿名分享链接访问文档。</summary>
    public const string ShareAccess = "share_access";

    /// <summary>已发布的全部访问类型键。</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        Download,
        Preview,
        ShareAccess,
    ]);
}

/// <summary>文档访问来源稳定键。</summary>
public static class HostDocumentAccessSourceKeys
{
    /// <summary>已认证 Host 用户通过受保护 API 访问。</summary>
    public const string Authenticated = "authenticated";

    /// <summary>匿名分享入口访问。</summary>
    public const string Share = "share";
}

/// <summary>
/// Host 文档访问日志稳定权限码。
/// </summary>
public static class HostDocumentAccessLogPermissions
{
    /// <summary>允许分页读取文档访问日志。</summary>
    public const string Read = "document.host_access_logs.read";
}

/// <summary>单条文档访问日志响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">访问日志记录标识。</param>
/// <param name="DocumentItemId">被访问文档项标识。</param>
/// <param name="DocumentTitle">访问时的文档标题快照。</param>
/// <param name="AccessTypeKey">访问类型键，取值见 <see cref="HostDocumentAccessTypeKeys"/>。</param>
/// <param name="SourceKey">访问来源键，取值见 <see cref="HostDocumentAccessSourceKeys"/>。</param>
/// <param name="ActorUserId">已认证访问者用户标识；匿名访问时为 <see langword="null"/>。</param>
/// <param name="OccurredAtUtc">访问发生时间（UTC）。</param>
/// <param name="ClientIpFingerprint">客户端 IP 指纹；匿名访问或未采集时为 <see langword="null"/>。</param>
public sealed record HostDocumentAccessLogResponse(
    Guid Id,
    Guid DocumentItemId,
    string DocumentTitle,
    string AccessTypeKey,
    string SourceKey,
    Guid? ActorUserId,
    DateTimeOffset OccurredAtUtc,
    string? ClientIpFingerprint);
