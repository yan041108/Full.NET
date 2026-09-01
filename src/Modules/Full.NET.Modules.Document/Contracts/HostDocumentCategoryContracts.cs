using System.Text.Json.Serialization;

namespace Full.NET.Modules.Document.Contracts;

/// <summary>
/// 创建文档分类的请求契约；<c>JsonUnmappedMemberHandling.Disallow</c> 保证协议字段严格匹配。
/// </summary>
/// <remarks>
/// 契约字段顺序为机器码的一部分；新增可选字段只能追加以避免位置参数漂移。
/// </remarks>
/// <param name="Name">分类展示名称。</param>
/// <param name="ParentId">父分类标识；null 表示创建顶级分类。</param>
/// <param name="SortOrder">同级内排序值，升序。</param>
/// <param name="Code">稳定分类编码，供程序化筛选；可为空。</param>
/// <param name="Icon">分类图标资源引用；可为空。</param>
/// <param name="Color">分类主题颜色；可为空。</param>
/// <param name="Description">分类说明；可为空。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[method: JsonConstructor]
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

/// <summary>
/// 更新文档分类的请求契约，使用乐观并发 Version 守卫。
/// </summary>
/// <remarks>
/// 契约字段顺序为机器码的一部分；新增可选字段只能追加在 Version 之前。
/// </remarks>
/// <param name="Name">分类展示名称。</param>
/// <param name="ParentId">父分类标识；null 表示提升为顶级。</param>
/// <param name="SortOrder">同级内排序值，升序。</param>
/// <param name="Code">稳定分类编码；传 null 表示清空既有编码。</param>
/// <param name="Icon">分类图标资源引用；传 null 表示清空。</param>
/// <param name="Color">分类主题颜色；传 null 表示清空。</param>
/// <param name="Description">分类说明；传 null 表示清空。</param>
/// <param name="Version">乐观并发版本号，必须等于当前行版本。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[method: JsonConstructor]
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

/// <summary>
/// 删除文档分类的请求契约，使用乐观并发 Version 守卫；不允许删除仍被文档引用的分类。
/// </summary>
/// <param name="Version">乐观并发版本号，必须等于当前行版本。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeleteHostDocumentCategoryRequest(long Version);

/// <summary>
/// 文档分类响应契约；字段顺序为稳定机器码的一部分。
/// </summary>
/// <param name="Id">分类标识。</param>
/// <param name="ParentId">父分类标识；null 表示顶级。</param>
/// <param name="Name">分类展示名称。</param>
/// <param name="SortOrder">同级内排序值，升序。</param>
/// <param name="Code">稳定分类编码；可为空。</param>
/// <param name="Icon">分类图标资源引用；可为空。</param>
/// <param name="Color">分类主题颜色；可为空。</param>
/// <param name="Description">分类说明；可为空。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最后更新时间（UTC），可空。</param>
/// <param name="Version">乐观并发版本号，用于后续更新、删除请求。</param>
[method: JsonConstructor]
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
