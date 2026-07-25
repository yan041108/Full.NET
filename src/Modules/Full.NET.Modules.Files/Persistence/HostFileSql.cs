using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Files.Persistence;

internal static class HostFileSql
{
    public static readonly SqlStatement CountActiveHostFiles = new(
        "files.count_active_host_files",
        """
        SELECT COUNT(1)
        FROM fn_files_file
        WHERE TenantId IS NULL
          AND DeletedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListActiveHostFilesSqlServer = new(
        "files.list_active_host_files.sql_server",
        """
        SELECT Id,
               OriginalFileName,
               ContentType,
               SizeBytes,
               ContentHash,
               CreatedAtUtc,
               CreatedByUserId
        FROM fn_files_file
        WHERE TenantId IS NULL
          AND DeletedAtUtc IS NULL
        ORDER BY CreatedAtUtc DESC, Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListActiveHostFilesMySql = new(
        "files.list_active_host_files.mysql",
        """
        SELECT Id,
               OriginalFileName,
               ContentType,
               SizeBytes,
               ContentHash,
               CreatedAtUtc,
               CreatedByUserId
        FROM fn_files_file
        WHERE TenantId IS NULL
          AND DeletedAtUtc IS NULL
        ORDER BY CreatedAtUtc DESC, Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindActiveById = new(
        "files.host_file.find_active_by_id",
        """
        SELECT Id,
               OriginalFileName,
               ContentType,
               SizeBytes,
               StorageKey,
               ContentHash,
               CreatedAtUtc,
               CreatedByUserId
        FROM fn_files_file
        WHERE Id = @FileId
          AND TenantId IS NULL
          AND DeletedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert = new(
        "files.host_file.insert",
        """
        INSERT INTO fn_files_file
            (Id, TenantId, OriginalFileName, ContentType, SizeBytes, StorageKey,
             ContentHash, CreatedAtUtc, CreatedByUserId, DeletedAtUtc)
        VALUES
            (@Id, NULL, @OriginalFileName, @ContentType, @SizeBytes, @StorageKey,
             @ContentHash, @CreatedAtUtc, @CreatedByUserId, NULL)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement SoftDelete = new(
        "files.host_file.soft_delete",
        """
        UPDATE fn_files_file
        SET DeletedAtUtc = @DeletedAtUtc
        WHERE Id = @FileId
          AND TenantId IS NULL
          AND DeletedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);
}
