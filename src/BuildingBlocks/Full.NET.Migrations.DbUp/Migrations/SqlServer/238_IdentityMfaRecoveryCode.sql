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
    CREATE INDEX IX_fn_identity_user_mfa_recovery_code_UserId
        ON dbo.fn_identity_user_mfa_recovery_code(UserId);
END;
