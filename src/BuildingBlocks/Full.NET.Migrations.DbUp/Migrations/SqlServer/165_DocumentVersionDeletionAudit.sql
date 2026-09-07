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
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档版本删除审计表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_deletion_audit';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_deletion_audit'), N'ContentHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_deletion_audit', @level2type=N'COLUMN', @level2name=N'ContentHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_deletion_audit'), N'DeletedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'删除时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_deletion_audit', @level2type=N'COLUMN', @level2name=N'DeletedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_deletion_audit'), N'DeletedBySourceKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'删除来源键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_deletion_audit', @level2type=N'COLUMN', @level2name=N'DeletedBySourceKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_deletion_audit'), N'DeletedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'删除人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_deletion_audit', @level2type=N'COLUMN', @level2name=N'DeletedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_deletion_audit'), N'DocumentItemId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档项标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_deletion_audit', @level2type=N'COLUMN', @level2name=N'DocumentItemId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_deletion_audit'), N'FileId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_deletion_audit', @level2type=N'COLUMN', @level2name=N'FileId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_deletion_audit'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_deletion_audit', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_deletion_audit'), N'SizeBytes', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'大小(字节)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_deletion_audit', @level2type=N'COLUMN', @level2name=N'SizeBytes';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_deletion_audit'), N'UploadedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'上传人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_deletion_audit', @level2type=N'COLUMN', @level2name=N'UploadedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_deletion_audit'), N'VersionCreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Version Created At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_deletion_audit', @level2type=N'COLUMN', @level2name=N'VersionCreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_deletion_audit'), N'VersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_deletion_audit', @level2type=N'COLUMN', @level2name=N'VersionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_deletion_audit'), N'VersionNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_deletion_audit', @level2type=N'COLUMN', @level2name=N'VersionNumber';

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
