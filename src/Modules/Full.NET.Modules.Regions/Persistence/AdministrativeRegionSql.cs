using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Regions.Persistence;

/// <summary>
/// 行政区域与数据集清单的 Dapper SQL 语句集，全部使用 <see cref="SqlDataScope.HostOnly"/>。
/// </summary>
internal static class AdministrativeRegionSql
{
    private const string Projection = """
        Id, ParentId, Code, Name, ShortName, MergerName, ZipCode, CityCode, Level,
        RegionType, PinYin, Longitude, Latitude, DisplayOrder, Remark,
        CreatedAtUtc, UpdatedAtUtc, Version
        """;

    private const string ListWhereClause = """
        (@ParentId IS NULL OR ParentId = @ParentId)
          AND (@Name IS NULL OR Name LIKE @NamePattern)
          AND (@Code IS NULL OR Code LIKE @CodePattern)
          AND (@Level IS NULL OR Level = @Level)
        """;

    public static readonly SqlStatement ListSqlServer = new(
        "regions.administrative_region.list.sql_server",
        $"""
        SELECT {Projection}
        FROM fn_regions_administrative_region
        WHERE {ListWhereClause}
        ORDER BY DisplayOrder, Name, Code, Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListMySql = new(
        "regions.administrative_region.list.mysql",
        $"""
        SELECT {Projection}
        FROM fn_regions_administrative_region
        WHERE {ListWhereClause}
        ORDER BY DisplayOrder, Name, Code, Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Count = new(
        "regions.administrative_region.count",
        $"""
        SELECT COUNT(*)
        FROM fn_regions_administrative_region
        WHERE {ListWhereClause}
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "regions.administrative_region.find_by_id",
        $"""
        SELECT {Projection}
        FROM fn_regions_administrative_region
        WHERE Id = @Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindByCode = new(
        "regions.administrative_region.find_by_code",
        $"""
        SELECT {Projection}
        FROM fn_regions_administrative_region
        WHERE Code = @Code
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListChildren = new(
        "regions.administrative_region.list_children",
        """
        SELECT region.Id, region.ParentId, region.Code, region.Name, region.Level, region.DisplayOrder,
               CASE WHEN EXISTS (
                   SELECT 1
                   FROM fn_regions_administrative_region AS child
                   WHERE child.ParentId = region.Id
               ) THEN 1 ELSE 0 END AS ChildCount
        FROM fn_regions_administrative_region AS region
        WHERE ((@ParentId IS NULL AND region.ParentId IS NULL) OR region.ParentId = @ParentId)
        ORDER BY region.DisplayOrder, region.Name, region.Code, region.Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListAllForTree = new(
        "regions.administrative_region.list_all_for_tree",
        """
        SELECT Id, ParentId, Code, Name, Level, DisplayOrder
        FROM fn_regions_administrative_region
        ORDER BY DisplayOrder, Name, Code, Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListParentLinks = new(
        "regions.administrative_region.list_parent_links",
        """
        SELECT Id, ParentId
        FROM fn_regions_administrative_region
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListCodeLinks = new(
        "regions.administrative_region.list_code_links",
        """
        SELECT region.Id, region.Code, parent.Code AS ParentCode
        FROM fn_regions_administrative_region AS region
        LEFT JOIN fn_regions_administrative_region AS parent
          ON parent.Id = region.ParentId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert = new(
        "regions.administrative_region.insert",
        """
        INSERT INTO fn_regions_administrative_region
            (Id, ParentId, Code, Name, ShortName, MergerName, ZipCode, CityCode, Level,
             RegionType, PinYin, Longitude, Latitude, DisplayOrder, Remark,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @ParentId, @Code, @Name, @ShortName, @MergerName, @ZipCode, @CityCode, @Level,
             @RegionType, @PinYin, @Longitude, @Latitude, @DisplayOrder, @Remark,
             @CreatedAtUtc, NULL, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Update = new(
        "regions.administrative_region.update",
        """
        UPDATE fn_regions_administrative_region
        SET ParentId = @ParentId,
            Name = @Name,
            ShortName = @ShortName,
            MergerName = @MergerName,
            ZipCode = @ZipCode,
            CityCode = @CityCode,
            Level = @Level,
            RegionType = @RegionType,
            PinYin = @PinYin,
            Longitude = @Longitude,
            Latitude = @Latitude,
            DisplayOrder = @DisplayOrder,
            Remark = @Remark,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateImport = new(
        "regions.administrative_region.update_import",
        """
        UPDATE fn_regions_administrative_region
        SET ParentId = @ParentId,
            Name = @Name,
            ShortName = @ShortName,
            MergerName = @MergerName,
            ZipCode = @ZipCode,
            CityCode = @CityCode,
            Level = @Level,
            RegionType = @RegionType,
            PinYin = @PinYin,
            Longitude = @Longitude,
            Latitude = @Latitude,
            DisplayOrder = @DisplayOrder,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteSubtreeSqlServer = new(
        "regions.administrative_region.delete_subtree.sql_server",
        """
        WITH descendants AS (
            SELECT Id
            FROM fn_regions_administrative_region
            WHERE Id = @Id
            UNION ALL
            SELECT child.Id
            FROM fn_regions_administrative_region AS child
            INNER JOIN descendants AS parent ON child.ParentId = parent.Id
        )
        DELETE FROM fn_regions_administrative_region
        WHERE Id IN (SELECT Id FROM descendants)
          AND EXISTS (
              SELECT 1
              FROM fn_regions_administrative_region AS root
              WHERE root.Id = @Id AND root.Version = @Version
          )
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteSubtreeMySql = new(
        "regions.administrative_region.delete_subtree.mysql",
        """
        WITH RECURSIVE descendants AS (
            SELECT Id
            FROM fn_regions_administrative_region
            WHERE Id = @Id
            UNION ALL
            SELECT child.Id
            FROM fn_regions_administrative_region AS child
            INNER JOIN descendants AS parent ON child.ParentId = parent.Id
        )
        DELETE FROM fn_regions_administrative_region
        WHERE Id IN (SELECT Id FROM descendants)
          AND EXISTS (
            SELECT 1
            FROM (
                SELECT Id
                FROM fn_regions_administrative_region
                WHERE Id = @Id AND Version = @Version
            ) AS root
          )
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteByCodesSqlServer = new(
        "regions.administrative_region.delete_by_codes.sql_server",
        """
        DELETE FROM fn_regions_administrative_region
        WHERE Code IN @Codes
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteByCodesMySql = new(
        "regions.administrative_region.delete_by_codes.mysql",
        """
        DELETE FROM fn_regions_administrative_region
        WHERE Code IN @Codes
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertManifest = new(
        "regions.dataset_manifest.insert",
        """
        INSERT INTO fn_regions_dataset_manifest
            (Id, DatasetKey, DatasetVersion, SourceDigest, RecordCount, AppliedAtUtc, AppliedByUserId)
        VALUES
            (@Id, @DatasetKey, @DatasetVersion, @SourceDigest, @RecordCount, @AppliedAtUtc, @AppliedByUserId)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindLatestManifest = new(
        "regions.dataset_manifest.find_latest",
        """
        SELECT TOP (1)
            Id, DatasetKey, DatasetVersion, SourceDigest, RecordCount, AppliedAtUtc, AppliedByUserId
        FROM fn_regions_dataset_manifest
        WHERE DatasetKey = @DatasetKey
        ORDER BY AppliedAtUtc DESC, Id DESC
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindLatestManifestMySql = new(
        "regions.dataset_manifest.find_latest.mysql",
        """
        SELECT Id, DatasetKey, DatasetVersion, SourceDigest, RecordCount, AppliedAtUtc, AppliedByUserId
        FROM fn_regions_dataset_manifest
        WHERE DatasetKey = @DatasetKey
        ORDER BY AppliedAtUtc DESC, Id
        LIMIT 1
        """,
        SqlDataScope.HostOnly);
}
