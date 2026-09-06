-- 159：国密密钥目录表。

IF OBJECT_ID(N'dbo.fn_cryptography_key', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_cryptography_key
    (
        Id uniqueidentifier NOT NULL,
        KeyKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DisplayName nvarchar(200) NOT NULL,
        Description nvarchar(1000) NULL,
        Algorithm varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Purpose varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PublicKeyHex varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PublicKeyFingerprint varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Status varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SortOrder int NOT NULL CONSTRAINT DF_fn_cryptography_key_SortOrder DEFAULT (0),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        CONSTRAINT PK_fn_cryptography_key PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UX_fn_cryptography_key_KeyKey UNIQUE (KeyKey),
        CONSTRAINT CK_fn_cryptography_key_Status
            CHECK (Status IN (N'active', N'retired'))
    );

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_cryptography_key')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'国密密钥目录表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_cryptography_key';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_cryptography_key')
      AND indexObject.name = N'IX_fn_cryptography_key_SortOrder'
)
    CREATE INDEX IX_fn_cryptography_key_SortOrder
        ON dbo.fn_cryptography_key(SortOrder, KeyKey);

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.fn_cryptography_key
    WHERE KeyKey = N'host-integration-signing'
)
    INSERT INTO dbo.fn_cryptography_key
        (Id, KeyKey, DisplayName, Description, Algorithm, Purpose,
         PublicKeyHex, PublicKeyFingerprint, Status, SortOrder, CreatedAtUtc)
    VALUES
        (
            '01956000-0001-7000-8000-000000000002',
            N'host-integration-signing',
            N'Host 集成载荷签名密钥',
            N'用于受控 SM2 签名/验签首个场景的 Host 级示例密钥；私钥仅通过部署配置注入。',
            N'sm2',
            N'integration-payload-signature',
            N'a17ff2e8117499f878cb5b81390a1f9c11384a32545463f577c674a3e233fd9b216dc58485160d62648e505b8e77d1bca31c79dfd7609caf28666d1b0454d90e',
            N'6dc3bfe99bbafa5ddafa8a4c47536c2e42d9ccc5548d417d221f2d3f952737e2',
            N'active',
            10,
            SYSUTCDATETIME()
        );
