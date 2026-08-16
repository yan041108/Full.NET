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
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证API 密钥表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'禁用时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'DisabledAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'DisplayName';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'IsActive';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'密钥哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'KeyHash';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'密钥前缀', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'KeyPrefix';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最后使用时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'LastUsedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'权限集合(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'PermissionsJson';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'UserId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_api_key', @level2type=N'COLUMN', @level2name=N'Version';

    CREATE UNIQUE INDEX UX_fn_identity_api_key_KeyHash
        ON dbo.fn_identity_api_key(KeyHash);

    CREATE INDEX IX_fn_identity_api_key_UserCreatedAtUtc
        ON dbo.fn_identity_api_key(UserId, CreatedAtUtc DESC, Id);
END;
