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
          AND StorageState = 'ready'
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
          AND StorageState = 'ready'
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
          AND StorageState = 'ready'
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
               ProviderKey,
               StorageKey,
               ContentHash,
               CreatedAtUtc,
               CreatedByUserId
        FROM fn_files_file
        WHERE Id = @FileId
          AND TenantId IS NULL
          AND StorageState = 'ready'
          AND DeletedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement LockHostFileRowSqlServer = new(
        "files.host_file.lock_row.sql_server",
        """
        SELECT Id
        FROM fn_files_file WITH (UPDLOCK, HOLDLOCK)
        WHERE Id = @FileId
          AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement LockHostFileRowMySql = new(
        "files.host_file.lock_row.mysql",
        """
        SELECT Id
        FROM fn_files_file
        WHERE Id = @FileId
          AND TenantId IS NULL
        FOR UPDATE
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert = new(
        "files.host_file.insert",
        """
        INSERT INTO fn_files_file
            (Id, TenantId, OriginalFileName, ContentType, SizeBytes, ProviderKey, StorageKey,
             ContentHash, StorageState, CreatedAtUtc, CreatedByUserId, DeletedAtUtc)
        VALUES
            (@Id, NULL, @OriginalFileName, @ContentType, @SizeBytes, @ProviderKey, @StorageKey,
             @ContentHash, 'pending', @CreatedAtUtc, @CreatedByUserId, NULL)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement MarkReady = new(
        "files.host_file.mark_ready",
        """
        UPDATE fn_files_file
        SET StorageState = 'ready'
        WHERE Id = @FileId
          AND TenantId IS NULL
          AND ProviderKey = @ProviderKey
          AND StorageKey = @StorageKey
          AND StorageState = 'publishing'
          AND DeletedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ClaimPublication = new(
        "files.host_file.claim_publication",
        """
        UPDATE fn_files_file
        SET StorageState = 'publishing'
        WHERE Id = @FileId
          AND TenantId IS NULL
          AND ProviderKey = @ProviderKey
          AND StorageKey = @StorageKey
          AND StorageState = 'pending'
          AND DeletedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ReconcileReady = new(
        "files.reconciliation.mark_ready",
        """
        UPDATE fn_files_file
        SET StorageState = 'ready'
        WHERE Id = @FileId
          AND TenantId IS NULL
          AND ProviderKey = @ProviderKey
          AND StorageKey = @StorageKey
          AND StorageState IN ('pending', 'publishing')
          AND DeletedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement SoftDelete = new(
        "files.host_file.soft_delete",
        """
        UPDATE fn_files_file
        SET DeletedAtUtc = @DeletedAtUtc
        WHERE Id = @FileId
          AND TenantId IS NULL
          AND StorageState = 'ready'
          AND DeletedAtUtc IS NULL
          AND NOT EXISTS (
              SELECT 1
              FROM fn_files_file_reference_claim
              WHERE FileId = @FileId
                AND (State = @PendingState OR State = @ActiveState))
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement SelectDeletedHostFilesSqlServer = new(
        "files.cleanup.select_deleted.sql_server",
        """
        SELECT TOP (@BatchSize)
               Id,
               ProviderKey,
               StorageKey,
               DeletedAtUtc
        FROM fn_files_file
        WHERE TenantId IS NULL
          AND DeletedAtUtc IS NOT NULL
          AND (@HasCursor = 0
               OR DeletedAtUtc > @AfterDeletedAtUtc
               OR (DeletedAtUtc = @AfterDeletedAtUtc AND Id > @AfterId))
        ORDER BY DeletedAtUtc, Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement SelectDeletedHostFilesMySql = new(
        "files.cleanup.select_deleted.my_sql",
        """
        SELECT Id,
               ProviderKey,
               StorageKey,
               DeletedAtUtc
        FROM fn_files_file
        WHERE TenantId IS NULL
          AND DeletedAtUtc IS NOT NULL
          AND (@HasCursor = 0
               OR DeletedAtUtc > @AfterDeletedAtUtc
               OR (DeletedAtUtc = @AfterDeletedAtUtc AND Id > @AfterId))
        ORDER BY DeletedAtUtc, Id
        LIMIT @BatchSize
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement PurgeDeletedHostFile = new(
        "files.cleanup.purge_deleted",
        """
        DELETE FROM fn_files_file
        WHERE Id = @FileId
          AND TenantId IS NULL
          AND ProviderKey = @ProviderKey
          AND StorageKey = @StorageKey
          AND DeletedAtUtc IS NOT NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement SelectPendingHostFilesSqlServer = new(
        "files.reconciliation.select_pending.sql_server",
        """
        SELECT TOP (@BatchSize)
               Id, ProviderKey, StorageKey, CreatedAtUtc, StorageState
        FROM fn_files_file
        WHERE TenantId IS NULL
          AND StorageState IN ('pending', 'publishing')
          AND DeletedAtUtc IS NULL
          AND CreatedAtUtc <= @CreatedBeforeUtc
          AND (@HasCursor = 0
               OR CreatedAtUtc > @AfterCreatedAtUtc
               OR (CreatedAtUtc = @AfterCreatedAtUtc AND Id > @AfterId))
        ORDER BY CreatedAtUtc, Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement SelectPendingHostFilesMySql = new(
        "files.reconciliation.select_pending.my_sql",
        """
        SELECT Id, ProviderKey, StorageKey, CreatedAtUtc, StorageState
        FROM fn_files_file
        WHERE TenantId IS NULL
          AND StorageState IN ('pending', 'publishing')
          AND DeletedAtUtc IS NULL
          AND CreatedAtUtc <= @CreatedBeforeUtc
          AND (@HasCursor = 0
               OR CreatedAtUtc > @AfterCreatedAtUtc
               OR (CreatedAtUtc = @AfterCreatedAtUtc AND Id > @AfterId))
        ORDER BY CreatedAtUtc, Id
        LIMIT @BatchSize
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement PurgePending = new(
        "files.reconciliation.purge_pending",
        """
        DELETE FROM fn_files_file
        WHERE Id = @FileId
          AND TenantId IS NULL
          AND ProviderKey = @ProviderKey
          AND StorageKey = @StorageKey
          AND StorageState = 'pending'
          AND DeletedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);
}
