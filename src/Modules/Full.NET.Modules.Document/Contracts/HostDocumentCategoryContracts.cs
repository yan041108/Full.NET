using System.Text.Json.Serialization;

namespace Full.NET.Modules.Document.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateHostDocumentCategoryRequest(
    string Name,
    Guid? ParentId,
    int SortOrder,
    string? Code,
    string? Icon,
    string? Color,
    string? Description);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateHostDocumentCategoryRequest(
    string Name,
    Guid? ParentId,
    int SortOrder,
    string? Code,
    string? Icon,
    string? Color,
    string? Description,
    long Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeleteHostDocumentCategoryRequest(long Version);

public sealed record HostDocumentCategoryResponse(
    Guid Id,
    Guid? ParentId,
    string Name,
    int SortOrder,
    string? Code,
    string? Icon,
    string? Color,
    string? Description,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    long Version);
