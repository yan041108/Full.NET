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

    CREATE UNIQUE INDEX UX_fn_identity_api_key_KeyHash
        ON dbo.fn_identity_api_key(KeyHash);

    CREATE INDEX IX_fn_identity_api_key_UserCreatedAtUtc
        ON dbo.fn_identity_api_key(UserId, CreatedAtUtc DESC, Id);
END;
