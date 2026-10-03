-- MFA 恢复码摘要表：仅保存哈希，明文仅在生成时返回一次。
IF OBJECT_ID(N'dbo.fn_identity_user_mfa_recovery_code', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_user_mfa_recovery_code
    (
        Id uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        CodeHash varbinary(32) NOT NULL,
        ConsumedAtUtc datetimeoffset(7) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        Version int NOT NULL CONSTRAINT DF_fn_identity_user_mfa_recovery_code_Version DEFAULT (1),
        CONSTRAINT PK_fn_identity_user_mfa_recovery_code PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_identity_user_mfa_recovery_code_UserId
            FOREIGN KEY (UserId) REFERENCES dbo.fn_identity_user(Id)
    );
END;

-- 注释与建表分开探测，已有表的未完成元数据步骤也能补齐。
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_mfa_recovery_code')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证 MFA 恢复码表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_mfa_recovery_code';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_mfa_recovery_code')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_mfa_recovery_code'), N'CodeHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'恢复码 SHA-256 摘要；不保存明文', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_mfa_recovery_code', @level2type=N'COLUMN', @level2name=N'CodeHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_mfa_recovery_code')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_mfa_recovery_code'), N'ConsumedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消费时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_mfa_recovery_code', @level2type=N'COLUMN', @level2name=N'ConsumedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_mfa_recovery_code')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_mfa_recovery_code'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_mfa_recovery_code', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_mfa_recovery_code')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_mfa_recovery_code'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_mfa_recovery_code', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_mfa_recovery_code')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_mfa_recovery_code'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_mfa_recovery_code', @level2type=N'COLUMN', @level2name=N'UserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_mfa_recovery_code')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_mfa_recovery_code'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_mfa_recovery_code', @level2type=N'COLUMN', @level2name=N'Version';
-- 索引独立恢复，避免表已存在时跳过未完成的建索引步骤。
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_identity_user_mfa_recovery_code')
      AND name = N'IX_fn_identity_user_mfa_recovery_code_UserId'
)
    CREATE INDEX IX_fn_identity_user_mfa_recovery_code_UserId
        ON dbo.fn_identity_user_mfa_recovery_code(UserId);
