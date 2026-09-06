-- 165：文档历史版本删除审计表。

IF OBJECT_ID(N'dbo.fn_document_version_deletion_audit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_document_version_deletion_audit
    (
        Id uniqueidentifier NOT NULL,
        DocumentItemId uniqueidentifier NOT NULL,
        VersionId uniqueidentifier NOT NULL,
        VersionNumber int NOT NULL,
        FileId uniqueidentifier NOT NULL,
        ContentHash char(64) NULL,
        SizeBytes bigint NOT NULL,
        UploadedByUserId uniqueidentifier NOT NULL,
        VersionCreatedAtUtc datetimeoffset(7) NOT NULL,
        DeletedAtUtc datetimeoffset(7) NOT NULL,
        DeletedByUserId uniqueidentifier NULL,
        DeletedBySourceKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CONSTRAINT PK_fn_document_version_deletion_audit PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_document_version_deletion_audit_SourceKey
            CHECK (DeletedBySourceKey IN (N'manual', N'retention')),
        CONSTRAINT CK_fn_document_version_deletion_audit_Number
            CHECK (VersionNumber > 0)
    );

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档历史版本删除审计表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_deletion_audit';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
      AND indexObject.name = N'IX_fn_document_version_deletion_audit_Item_DeletedAt'
)
    CREATE INDEX IX_fn_document_version_deletion_audit_Item_DeletedAt
        ON dbo.fn_document_version_deletion_audit(DocumentItemId, DeletedAtUtc DESC);

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
      AND indexObject.name = N'UX_fn_document_version_deletion_audit_VersionId'
)
    CREATE UNIQUE INDEX UX_fn_document_version_deletion_audit_VersionId
        ON dbo.fn_document_version_deletion_audit(VersionId);
