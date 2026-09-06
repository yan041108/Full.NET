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
