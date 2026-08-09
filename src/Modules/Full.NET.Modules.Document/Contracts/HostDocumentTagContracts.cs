using System.Text.Json.Serialization;

namespace Full.NET.Modules.Document.Contracts;

/// <summary>
/// 创建主机文档标签的请求契约。新增 Code/Icon/Description 以与 Category 统一字段集对齐。
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateHostDocumentTagRequest(
    string Name,
    string? Code,
    string? Icon,
    string? Color,
    string? Description)
{
    /// <summary>
    /// 保留原标签创建构造方式，新增展示字段缺省为空。
    /// </summary>
    public CreateHostDocumentTagRequest(string name)
        : this(name, null, null, null, null)
    {
    }
}

/// <summary>
/// 更新主机文档标签的请求契约。新增 Code/Icon/Description 以与 Category 统一字段集对齐。
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateHostDocumentTagRequest(
    string Name,
    string? Code,
    string? Icon,
    string? Color,
    string? Description,
    long Version)
{
    /// <summary>
    /// 保留原标签更新构造方式，新增展示字段缺省为空。
    /// </summary>
    public UpdateHostDocumentTagRequest(string name, long version)
        : this(name, null, null, null, null, version)
    {
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeleteHostDocumentTagRequest(long Version);

/// <summary>
/// 主机文档标签的响应契约。新增 Code/Icon/Description 以与 Category 统一字段集对齐；
/// 与 QueryService.Map 的构造参数顺序保持一致：Id/Name/Code/Icon/Color/Description/UseCount。
/// </summary>
public sealed record HostDocumentTagResponse(
    Guid Id,
    string Name,
    string? Code,
    string? Icon,
    string? Color,
    string? Description,
    int UseCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    long Version)
{
    /// <summary>
    /// 兼容策略：保留扩展前的旧构造签名，避免新增 Code/Icon/Color/Description/UseCount
    /// 导致既有 .NET 调用方出现"构造参数数不匹配(CS8852)"编译错误。
    /// UseCount 默认补 0，表示未统计使用次数的旧数据。
    /// </summary>
    [Obsolete("保留用于源码兼容；建议使用带完整字段的构造函数")]
    public HostDocumentTagResponse(
        Guid id,
        string name,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? updatedAtUtc,
        long version)
        : this(
            id,
            name,
            null,
            null,
            null,
            null,
            0,
            createdAtUtc,
            updatedAtUtc,
            version)
    {
    }
}
