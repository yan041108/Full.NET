using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Files.Persistence;

/// <summary>Host 虚拟目录的参数化 SQL 集合，全部声明 <c>SqlDataScope.HostOnly</c>。</summary>
internal static class HostFolderSql
{
    public static readonly SqlStatement ListChildrenSqlServer = new(
        "files.folder.list_children.sqlserver",
        """
        SELECT TOP (@TakeCount) f.Id,f.ParentId,f.Name,f.DisplayOrder,f.Revision,
            CASE WHEN EXISTS(SELECT 1 FROM fn_files_folder AS c WHERE c.ParentId=f.Id AND c.TenantId IS NULL AND c.DeletedAtUtc IS NULL) THEN 1 ELSE 0 END AS HasChildren
        FROM fn_files_folder AS f
        WHERE f.TenantId IS NULL AND f.DeletedAtUtc IS NULL
          AND ((@ParentId IS NULL AND f.ParentId IS NULL) OR f.ParentId=@ParentId)
        ORDER BY f.DisplayOrder,f.Name,f.Id
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement AncestorDepthSqlServer = new(
        "files.folder.ancestor_depth.sqlserver",
        """
        WITH ancestors AS (
            SELECT Id,ParentId,1 AS Depth FROM fn_files_folder
            WHERE Id=@FolderId AND TenantId IS NULL AND DeletedAtUtc IS NULL
            UNION ALL
            SELECT p.Id,p.ParentId,a.Depth+1 FROM fn_files_folder AS p
            INNER JOIN ancestors AS a ON p.Id=a.ParentId
            WHERE p.TenantId IS NULL AND p.DeletedAtUtc IS NULL AND a.Depth < @MaxDepth
        )
        SELECT COALESCE(MAX(Depth),0) FROM ancestors
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement ListChildrenMySql = new(
        "files.folder.list_children.mysql",
        """
        SELECT f.Id,f.ParentId,f.Name,f.DisplayOrder,f.Revision,
            CASE WHEN EXISTS(SELECT 1 FROM fn_files_folder AS c WHERE c.ParentId=f.Id AND c.TenantId IS NULL AND c.DeletedAtUtc IS NULL) THEN 1 ELSE 0 END AS HasChildren
        FROM fn_files_folder AS f
        WHERE f.TenantId IS NULL AND f.DeletedAtUtc IS NULL
          AND ((@ParentId IS NULL AND f.ParentId IS NULL) OR f.ParentId=@ParentId)
        ORDER BY f.DisplayOrder,f.Name,f.Id LIMIT @TakeCount
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement AncestorDepthMySql = new(
        "files.folder.ancestor_depth.mysql",
        """
        WITH RECURSIVE ancestors AS (
            SELECT Id,ParentId,1 AS Depth FROM fn_files_folder
            WHERE Id=@FolderId AND TenantId IS NULL AND DeletedAtUtc IS NULL
            UNION ALL
            SELECT p.Id,p.ParentId,a.Depth+1 FROM fn_files_folder AS p
            INNER JOIN ancestors AS a ON p.Id=a.ParentId
            WHERE p.TenantId IS NULL AND p.DeletedAtUtc IS NULL AND a.Depth < @MaxDepth
        )
        SELECT COALESCE(MAX(Depth),0) FROM ancestors
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement FindActiveById = new(
        "files.folder.find_active_by_id",
        """
        SELECT Id,
               ParentId,
               Name,
               DisplayOrder,
               Revision,
               CreatedAtUtc,
               CreatedByUserId,
               UpdatedAtUtc,
               UpdatedByUserId
        FROM fn_files_folder
        WHERE Id = @FolderId
          AND TenantId IS NULL
          AND DeletedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert = new(
        "files.folder.insert",
        """
        INSERT INTO fn_files_folder
            (Id, TenantId, ParentId, Name, DisplayOrder, Revision,
             CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId, DeletedAtUtc)
        VALUES
            (@Id, NULL, @ParentId, @Name, @DisplayOrder, 0,
             @CreatedAtUtc, @CreatedByUserId, NULL, NULL, NULL)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Update = new(
        "files.folder.update",
        """
        UPDATE fn_files_folder
        SET Name = @Name,
            DisplayOrder = @DisplayOrder,
            Revision = Revision + 1,
            UpdatedAtUtc = @UpdatedAtUtc,
            UpdatedByUserId = @UpdatedByUserId
        WHERE Id = @FolderId
          AND TenantId IS NULL
          AND DeletedAtUtc IS NULL
          AND Revision = @ExpectedRevision
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement SoftDelete = new(
        "files.folder.soft_delete",
        """
        UPDATE fn_files_folder
        SET DeletedAtUtc = @DeletedAtUtc,
            UpdatedAtUtc = @DeletedAtUtc,
            UpdatedByUserId = @UpdatedByUserId,
            Revision = Revision + 1
        WHERE Id = @FolderId
          AND TenantId IS NULL
          AND DeletedAtUtc IS NULL
          AND Revision = @ExpectedRevision
          AND NOT EXISTS (
              SELECT 1
              FROM (
                  SELECT child.Id
                  FROM fn_files_folder AS child
                  WHERE child.ParentId = @FolderId
                    AND child.TenantId IS NULL
                    AND child.DeletedAtUtc IS NULL) AS blocked_children)
          AND NOT EXISTS (
              SELECT 1
              FROM fn_files_file AS active_file
              WHERE active_file.FolderId = @FolderId
                AND active_file.TenantId IS NULL
                AND active_file.DeletedAtUtc IS NULL
                AND active_file.StorageState = 'ready')
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ExistsActiveById = new(
        "files.folder.exists_active_by_id",
        """
        SELECT COUNT(1)
        FROM fn_files_folder
        WHERE Id = @FolderId
          AND TenantId IS NULL
          AND DeletedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountActiveChildren = new(
        "files.folder.count_active_children",
        """
        SELECT COUNT(1)
        FROM fn_files_folder
        WHERE ParentId = @FolderId
          AND TenantId IS NULL
          AND DeletedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountActiveFiles = new(
        "files.folder.count_active_files",
        """
        SELECT COUNT(1)
        FROM fn_files_file
        WHERE FolderId = @FolderId
          AND TenantId IS NULL
          AND DeletedAtUtc IS NULL
          AND StorageState = 'ready'
        """,
        SqlDataScope.HostOnly);
}
