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
    string? Description)
{
    /// <summary>
    /// 保留原分类创建构造方式，新增展示字段缺省为空。
    /// </summary>
    public CreateHostDocumentCategoryRequest(string name, Guid? parentId, int sortOrder)
        : this(name, parentId, sortOrder, null, null, null, null)
    {
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateHostDocumentCategoryRequest(
    string Name,
    Guid? ParentId,
    int SortOrder,
    string? Code,
    string? Icon,
    string? Color,
    string? Description,
    long Version)
{
    /// <summary>
    /// 保留原分类更新构造方式，新增展示字段缺省为空。
    /// </summary>
    public UpdateHostDocumentCategoryRequest(
        string name,
        Guid? parentId,
        int sortOrder,
        long version)
        : this(name, parentId, sortOrder, null, null, null, null, version)
    {
    }
}

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
