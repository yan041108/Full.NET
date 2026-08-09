namespace Full.NET.Modules.Document.Persistence;

/// <summary>
/// 文档标签持久化记录。新增 Code/Icon/Description 以与 Category 统一字段集对齐，属性顺序保持 Code, Icon, Color, Description。
/// </summary>
internal sealed class DocumentTagRecord
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 标签编码，用于稳定机器标识与外部集成。
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// 标签图标，存储图标类名或资源路径。
    /// </summary>
    public string? Icon { get; init; }

    public string? Color { get; init; }

    /// <summary>
    /// 标签描述文本，最长 500 字符。
    /// </summary>
    public string? Description { get; init; }

    public int UseCount { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? UpdatedAtUtc { get; init; }

    public long Version { get; init; }
}
