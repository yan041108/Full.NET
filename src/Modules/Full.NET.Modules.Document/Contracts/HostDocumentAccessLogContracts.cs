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
public sealed record HostDocumentAccessLogResponse(
    Guid Id,
    Guid DocumentItemId,
    string DocumentTitle,
    string AccessTypeKey,
    string SourceKey,
    Guid? ActorUserId,
    DateTimeOffset OccurredAtUtc,
    string? ClientIpFingerprint);
