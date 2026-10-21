namespace Full.NET.Modules.Files.Persistence;

/// <summary>直接子目录的数据库投影；属性赋值支持双库整数存在性结果到布尔的转换，不要求 DTO 构造器类型完全匹配。</summary>
internal sealed class HostFolderChildRecord
{
    public Guid Id { get; init; }
    public Guid? ParentId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int DisplayOrder { get; init; }
    public long Revision { get; init; }
    public bool HasChildren { get; init; }
}
