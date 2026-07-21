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
END;
