-- 138：OpenAccess 接入方应用主表；凭据仍复用 fn_identity_api_key。

IF OBJECT_ID(N'dbo.fn_identity_open_access_client', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_open_access_client
    (
        Id uniqueidentifier NOT NULL,
        ApiKeyId uniqueidentifier NOT NULL,
        Name nvarchar(128) NOT NULL,
        Description nvarchar(512) NULL,
        Remark nvarchar(256) NULL,
        CreatedByUserId uniqueidentifier NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_identity_open_access_client_Version DEFAULT (1),
        CONSTRAINT PK_fn_identity_open_access_client PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UX_fn_identity_open_access_client_ApiKeyId UNIQUE (ApiKeyId),
        CONSTRAINT FK_fn_identity_open_access_client_ApiKey
            FOREIGN KEY (ApiKeyId) REFERENCES dbo.fn_identity_api_key (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_open_access_client')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证开放访问客户端表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_open_access_client';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_open_access_client')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_open_access_client'), N'ApiKeyId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'API 密钥标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_open_access_client', @level2type=N'COLUMN', @level2name=N'ApiKeyId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_open_access_client')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_open_access_client'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_open_access_client', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_open_access_client')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_open_access_client'), N'CreatedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_open_access_client', @level2type=N'COLUMN', @level2name=N'CreatedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_open_access_client')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_open_access_client'), N'Description', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'描述', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_open_access_client', @level2type=N'COLUMN', @level2name=N'Description';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_open_access_client')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_open_access_client'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_open_access_client', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_open_access_client')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_open_access_client'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_open_access_client', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_open_access_client')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_open_access_client'), N'Remark', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'备注', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_open_access_client', @level2type=N'COLUMN', @level2name=N'Remark';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_open_access_client')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_open_access_client'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_open_access_client', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_open_access_client')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_open_access_client'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_open_access_client', @level2type=N'COLUMN', @level2name=N'Version';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_open_access_client')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'OpenAccess 接入方应用表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_open_access_client';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_identity_open_access_client')
      AND indexObject.name = N'IX_fn_identity_open_access_client_Name'
)
    CREATE INDEX IX_fn_identity_open_access_client_Name
        ON dbo.fn_identity_open_access_client(Name, CreatedAtUtc DESC, Id DESC);
