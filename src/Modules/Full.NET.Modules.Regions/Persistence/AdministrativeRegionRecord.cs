namespace Full.NET.Modules.Regions.Persistence;

/// <summary>行政区域持久化记录，与 <c>fn_regions_administrative_region</c> 投影对齐。</summary>
internal sealed class AdministrativeRegionRecord
{
    public Guid Id { get; init; }

    public Guid? ParentId { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? ShortName { get; init; }

    public string? MergerName { get; init; }

    public string? ZipCode { get; init; }

    public string? CityCode { get; init; }

    public int Level { get; init; }

    public string? RegionType { get; init; }

    public string? PinYin { get; init; }

    public decimal? Longitude { get; init; }

    public decimal? Latitude { get; init; }

    public int DisplayOrder { get; init; }

    public string? Remark { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? UpdatedAtUtc { get; init; }

    public int Version { get; init; }
}

/// <summary>父子链接投影，用于环检测。</summary>
internal sealed class AdministrativeRegionParentLinkRecord
{
    public Guid Id { get; init; }

    public Guid? ParentId { get; init; }
}

/// <summary>导入差异计算使用的编码与父编码投影。</summary>
internal sealed class AdministrativeRegionCodeLinkRecord
{
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string? ParentCode { get; init; }
}

/// <summary>级联子节点查询投影。</summary>
internal sealed class AdministrativeRegionChildQueryRecord
{
    public Guid Id { get; init; }

    public Guid? ParentId { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Level { get; init; }

    public int DisplayOrder { get; init; }

    public int ChildCount { get; init; }
}

/// <summary>树构建使用的轻量投影。</summary>
internal sealed class AdministrativeRegionTreeRecord
{
    public Guid Id { get; init; }

    public Guid? ParentId { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Level { get; init; }

    public int DisplayOrder { get; init; }
}

/// <summary>数据集清单持久化记录。</summary>
internal sealed class DatasetManifestRecord
{
    public Guid Id { get; init; }

    public string DatasetKey { get; init; } = string.Empty;

    public string DatasetVersion { get; init; } = string.Empty;

    public string SourceDigest { get; init; } = string.Empty;

    public int RecordCount { get; init; }

    public DateTimeOffset AppliedAtUtc { get; init; }

    public Guid AppliedByUserId { get; init; }
}
