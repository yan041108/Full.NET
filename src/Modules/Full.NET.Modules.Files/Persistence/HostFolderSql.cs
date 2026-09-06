using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Files.Persistence;

/// <summary>Host 虚拟目录的参数化 SQL 集合，全部声明 <c>SqlDataScope.HostOnly</c>。</summary>
internal static class HostFolderSql
{
    public static readonly SqlStatement ListActive = new(
        "files.folder.list_active",
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
        WHERE TenantId IS NULL
          AND DeletedAtUtc IS NULL
        ORDER BY DisplayOrder, Name, Id
        """,
        SqlDataScope.HostOnly);

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
              FROM fn_files_folder AS child
              WHERE child.ParentId = @FolderId
                AND child.TenantId IS NULL
                AND child.DeletedAtUtc IS NULL)
          AND NOT EXISTS (
              SELECT 1
              FROM fn_files_file AS file
              WHERE file.FolderId = @FolderId
                AND file.TenantId IS NULL
                AND file.DeletedAtUtc IS NULL
                AND file.StorageState = 'ready')
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
