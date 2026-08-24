-- 为 Host 账号持久化 TOTP 强认证密钥（Data Protection 密文）。
-- UserId 与 fn_identity_user 一对一；确认前 IsEnabled=0，确认后启用。
-- SQL Server：UserId 聚集主键（低基数账号级表，按用户点查为主）。

IF OBJECT_ID(N'dbo.fn_identity_user_totp', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_user_totp
    (
        UserId uniqueidentifier NOT NULL,
        SecretProtected nvarchar(512) NOT NULL,
        IsEnabled bit NOT NULL CONSTRAINT DF_fn_identity_user_totp_IsEnabled DEFAULT (0),
        ConfirmedAtUtc datetimeoffset(7) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL CONSTRAINT DF_fn_identity_user_totp_Version DEFAULT (1),
        CONSTRAINT PK_fn_identity_user_totp PRIMARY KEY CLUSTERED (UserId),
        CONSTRAINT FK_fn_identity_user_totp_UserId
            FOREIGN KEY (UserId) REFERENCES dbo.fn_identity_user(Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_totp')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证用户 TOTP表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_totp';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_totp')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_totp'), N'ConfirmedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'确认时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_totp', @level2type=N'COLUMN', @level2name=N'ConfirmedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_totp')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_totp'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_totp', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_totp')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_totp'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_totp', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_totp')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_totp'), N'SecretProtected', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'受保护密钥', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_totp', @level2type=N'COLUMN', @level2name=N'SecretProtected';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_totp')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_totp'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_totp', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_totp')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_totp'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_totp', @level2type=N'COLUMN', @level2name=N'UserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_totp')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_totp'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_totp', @level2type=N'COLUMN', @level2name=N'Version';
END;
