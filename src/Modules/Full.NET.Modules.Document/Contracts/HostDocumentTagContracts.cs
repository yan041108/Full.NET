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
    string? Description);

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
    long Version);

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
