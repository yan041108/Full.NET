using System.Text.Json.Serialization;

namespace Full.NET.Modules.Document.Contracts;

/// <summary>文档版本删除来源稳定键。</summary>
public static class HostDocumentVersionDeletionSourceKeys
{
    /// <summary>管理员通过授权 API 手动删除。</summary>
    public const string Manual = "manual";

    /// <summary>保留策略 Worker 自动裁剪。</summary>
    public const string Retention = "retention";
}

/// <summary>删除历史版本的请求契约，携带文档项乐观并发版本。</summary>
/// <param name="Version">文档项乐观并发版本号。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeleteHostDocumentVersionRequest(long Version);
