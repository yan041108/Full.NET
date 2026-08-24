-- 031：Host 作用域 API Key 凭据（仅保存哈希）。

IF OBJECT_ID(N'dbo.fn_identity_api_key', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_api_key
    (
        Id uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        DisplayName nvarchar(128) NOT NULL,
        KeyPrefix varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        KeyHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PermissionsJson nvarchar(4000) NOT NULL,
        ExpiresAtUtc datetimeoffset(7) NULL,
        IsActive bit NOT NULL,
        LastUsedAtUtc datetimeoffset(7) NULL,
        DisabledAtUtc datetimeoffset(7) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_identity_api_key_Version DEFAULT (1),
        CONSTRAINT PK_fn_identity_api_key PRIMARY KEY CLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_api_key')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证API 密钥表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_api_key')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_api_key'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_api_key')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_api_key'), N'DisabledAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'禁用时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'DisabledAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_api_key')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_api_key'), N'DisplayName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'DisplayName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_api_key')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_api_key'), N'ExpiresAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_api_key')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_api_key'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_api_key')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_api_key'), N'IsActive', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'IsActive';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_api_key')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_api_key'), N'KeyHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'密钥哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'KeyHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_api_key')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_api_key'), N'KeyPrefix', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'密钥前缀', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'KeyPrefix';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_api_key')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_api_key'), N'LastUsedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最后使用时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'LastUsedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_api_key')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_api_key'), N'PermissionsJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'权限集合(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'PermissionsJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_api_key')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_api_key'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_api_key')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_api_key'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'UserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_api_key')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_api_key'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'Version';

    CREATE UNIQUE INDEX UX_fn_identity_api_key_KeyHash
        ON dbo.fn_identity_api_key(KeyHash);

    CREATE INDEX IX_fn_identity_api_key_UserCreatedAtUtc
        ON dbo.fn_identity_api_key(UserId, CreatedAtUtc DESC, Id);
END;
