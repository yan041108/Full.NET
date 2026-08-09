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
    long Version)
{
    /// <summary>
    /// 兼容策略：保留扩展前的旧构造签名，避免新增 Code/Icon/Color/Description
    /// 导致既有 .NET 调用方出现"构造参数数不匹配(CS8852)"编译错误。
    /// 新代码建议使用主构造，显式填充全部展示字段。
    /// </summary>
    [Obsolete("保留用于源码兼容；建议使用带完整字段的构造函数")]
    public HostDocumentCategoryResponse(
        Guid id,
        Guid? parentId,
        string name,
        int sortOrder,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? updatedAtUtc,
        long version)
        : this(
            id,
            parentId,
            name,
            sortOrder,
            null,
            null,
            null,
            null,
            createdAtUtc,
            updatedAtUtc,
            version)
    {
    }
}
