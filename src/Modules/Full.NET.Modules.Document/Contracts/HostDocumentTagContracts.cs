using System.Text.Json.Serialization;

namespace Full.NET.Modules.Document.Contracts;

/// <summary>
/// 创建主机文档标签的请求契约。新增 Code/Icon/Description 以与 Category 统一字段集对齐。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Name">标签显示名称。</param>
/// <param name="Code">标签稳定编码；可选，发布后不可改名。</param>
/// <param name="Icon">标签图标标识；可选，前端据此渲染图标。</param>
/// <param name="Color">标签主题色；可选，前端据此渲染颜色。</param>
/// <param name="Description">标签描述；可选。</param>
/// <param name="IsHot">是否标记为热门；默认 <see langword="false"/>。</param>
/// <param name="IsRecommended">是否标记为推荐；默认 <see langword="false"/>。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[method: JsonConstructor]
public sealed record CreateHostDocumentTagRequest(
    string Name,
    string? Code,
    string? Icon,
    string? Color,
    string? Description,
    bool IsHot = false,
    bool IsRecommended = false)
{
    /// <summary>
    /// 保留原标签创建构造方式，新增展示字段缺省为空。
    /// </summary>
    public CreateHostDocumentTagRequest(string name)
        : this(name, null, null, null, null, false, false)
    {
    }
}

/// <summary>
/// 更新主机文档标签的请求契约。新增 Code/Icon/Description 以与 Category 统一字段集对齐。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Name">标签显示名称。</param>
/// <param name="Code">标签稳定编码；可选，发布后不可改名。</param>
/// <param name="Icon">标签图标标识；可选。</param>
/// <param name="Color">标签主题色；可选。</param>
/// <param name="Description">标签描述；可选。</param>
/// <param name="Version">CAS 乐观并发期望值；必须等于当前行版本，否则更新失败。</param>
/// <param name="IsHot">是否标记为热门；默认 <see langword="false"/>。</param>
/// <param name="IsRecommended">是否标记为推荐；默认 <see langword="false"/>。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[method: JsonConstructor]
public sealed record UpdateHostDocumentTagRequest(
    string Name,
    string? Code,
    string? Icon,
    string? Color,
    string? Description,
    long Version,
    bool IsHot = false,
    bool IsRecommended = false)
{
    /// <summary>
    /// 保留原标签更新构造方式，新增展示字段缺省为空。
    /// </summary>
    public UpdateHostDocumentTagRequest(string name, long version)
        : this(name, null, null, null, null, version, false, false)
    {
    }
}

/// <summary>
/// 删除文档标签的请求契约，使用乐观并发 Version 守卫；不允许删除仍被文档引用的标签。
/// </summary>
/// <param name="Version">乐观并发版本号，必须等于当前行版本。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeleteHostDocumentTagRequest(long Version);

/// <summary>
/// 主机文档标签的响应契约。新增 Code/Icon/Description 以与 Category 统一字段集对齐；
/// 与 QueryService.Map 的构造参数顺序保持一致：Id/Name/Code/Icon/Color/Description/UseCount。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">标签唯一标识。</param>
/// <param name="Name">标签显示名称。</param>
/// <param name="Code">标签稳定编码；未设置时为 <see langword="null"/>。</param>
/// <param name="Icon">标签图标标识；未设置时为 <see langword="null"/>。</param>
/// <param name="Color">标签主题色；未设置时为 <see langword="null"/>。</param>
/// <param name="Description">标签描述；未设置时为 <see langword="null"/>。</param>
/// <param name="UseCount">标签被文档引用次数；旧数据可能为 0。</param>
/// <param name="CreatedAtUtc">标签创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">标签最近更新时间（UTC）；未更新时为 <see langword="null"/>。</param>
/// <param name="Version">CAS 乐观并发版本号。</param>
/// <param name="IsHot">是否标记为热门。</param>
/// <param name="IsRecommended">是否标记为推荐。</param>
[method: JsonConstructor]
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
    long Version,
    bool IsHot,
    bool IsRecommended)
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
            version,
            false,
            false)
    {
    }
}
