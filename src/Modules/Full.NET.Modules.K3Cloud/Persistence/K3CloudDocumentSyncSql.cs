using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.K3Cloud.Persistence;

/// <summary>K3Cloud 单据同步 SQL。</summary>
internal static class K3CloudDocumentSyncSql
{
    private const string Columns = """
        Id, ConnectionConfigId, DocumentTypeKey, BusinessKey, PayloadJson, StatusKey, LastStepKey,
        ExternalBillId, ExternalBillNo, LastErrorCode, LastErrorMessage, SubmittedAtUtc,
        CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, Version
        """;

    public static readonly SqlStatement Insert = new(
        "k3cloud.document_sync.insert",
        $"""
        INSERT INTO fn_k3cloud_document_sync
            ({Columns})
        VALUES
            (@Id, @ConnectionConfigId, @DocumentTypeKey, @BusinessKey, @PayloadJson, @StatusKey, @LastStepKey,
             @ExternalBillId, @ExternalBillNo, @LastErrorCode, @LastErrorMessage, @SubmittedAtUtc,
             @CreatedAtUtc, @UpdatedAtUtc, @CreatedByUserId, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Update = new(
        "k3cloud.document_sync.update",
        """
        UPDATE fn_k3cloud_document_sync
        SET StatusKey = @StatusKey,
            LastStepKey = @LastStepKey,
            ExternalBillId = @ExternalBillId,
            ExternalBillNo = @ExternalBillNo,
            LastErrorCode = @LastErrorCode,
            LastErrorMessage = @LastErrorMessage,
            SubmittedAtUtc = @SubmittedAtUtc,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    /// <summary>领取可重试的同步记录，提交后才允许再次调用金蝶。</summary>
    public static readonly SqlStatement ClaimRetry = new(
        "k3cloud.document_sync.claim_retry",
        """
        UPDATE fn_k3cloud_document_sync
        SET StatusKey = 'pending',
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND Version = @Version
          AND StatusKey IN ('save_failed', 'submit_failed', 'save_succeeded')
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "k3cloud.document_sync.find_by_id",
        $"""
        SELECT {Columns}
        FROM fn_k3cloud_document_sync
        WHERE Id = @SyncId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindByBusinessKey = new(
        "k3cloud.document_sync.find_by_business_key",
        $"""
        SELECT {Columns}
        FROM fn_k3cloud_document_sync
        WHERE ConnectionConfigId = @ConnectionConfigId
          AND DocumentTypeKey = @DocumentTypeKey
          AND BusinessKey = @BusinessKey
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Count = new(
        "k3cloud.document_sync.count",
        """
        SELECT COUNT(1)
        FROM fn_k3cloud_document_sync
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListSqlServer = new(
        "k3cloud.document_sync.list.sqlserver",
        $"""
        SELECT {Columns}
        FROM fn_k3cloud_document_sync
        ORDER BY CreatedAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListMySql = new(
        "k3cloud.document_sync.list.mysql",
        $"""
        SELECT {Columns}
        FROM fn_k3cloud_document_sync
        ORDER BY CreatedAtUtc DESC, Id DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);
}
