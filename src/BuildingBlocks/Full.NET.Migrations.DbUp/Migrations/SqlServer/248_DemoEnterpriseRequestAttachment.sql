-- 248：只扩展申请附件引用，不关联 Files 外键；索引与注释独立收敛，支持未记账重入。
IF OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.demo_enterprise_request_request_attachment (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        RequestId uniqueidentifier NOT NULL,
        FileId uniqueidentifier NOT NULL,
        OriginalFileName nvarchar(255) NOT NULL,
        SizeBytes bigint NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CreatedById uniqueidentifier NOT NULL,
        StateKey nvarchar(16) NOT NULL,
        UploadExpiresAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_demo_enterprise_request_request_attachment PRIMARY KEY NONCLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'企业申请附件引用表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_request_attachment';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_request_attachment', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment'), N'CreatedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_request_attachment', @level2type=N'COLUMN', @level2name=N'CreatedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment'), N'FileId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_request_attachment', @level2type=N'COLUMN', @level2name=N'FileId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_request_attachment', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment'), N'OriginalFileName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'下载文件名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_request_attachment', @level2type=N'COLUMN', @level2name=N'OriginalFileName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment'), N'RequestId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属申请标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_request_attachment', @level2type=N'COLUMN', @level2name=N'RequestId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment'), N'SizeBytes', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文件字节数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_request_attachment', @level2type=N'COLUMN', @level2name=N'SizeBytes';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment'), N'StateKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'附件绑定状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_request_attachment', @level2type=N'COLUMN', @level2name=N'StateKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属租户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_request_attachment', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment'), N'UploadExpiresAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'上传保护过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_request_attachment', @level2type=N'COLUMN', @level2name=N'UploadExpiresAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'企业申请附件引用表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_request_attachment';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_request_attachment', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment'), N'CreatedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_request_attachment', @level2type=N'COLUMN', @level2name=N'CreatedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment'), N'FileId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_request_attachment', @level2type=N'COLUMN', @level2name=N'FileId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_request_attachment', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment'), N'OriginalFileName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'下载文件名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_request_attachment', @level2type=N'COLUMN', @level2name=N'OriginalFileName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment'), N'RequestId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属申请标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_request_attachment', @level2type=N'COLUMN', @level2name=N'RequestId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment'), N'SizeBytes', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文件字节数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_request_attachment', @level2type=N'COLUMN', @level2name=N'SizeBytes';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属租户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_request_attachment', @level2type=N'COLUMN', @level2name=N'TenantId';
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment') AND name = N'IX_demo_enterprise_request_request_attachment_Resource')
    CREATE CLUSTERED INDEX IX_demo_enterprise_request_request_attachment_Resource ON dbo.demo_enterprise_request_request_attachment(TenantId, RequestId, CreatedAtUtc, Id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.demo_enterprise_request_request_attachment') AND name = N'UX_demo_enterprise_request_request_attachment_FileId')
    CREATE UNIQUE INDEX UX_demo_enterprise_request_request_attachment_FileId ON dbo.demo_enterprise_request_request_attachment(FileId);
