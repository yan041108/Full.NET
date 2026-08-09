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
    long Version);
