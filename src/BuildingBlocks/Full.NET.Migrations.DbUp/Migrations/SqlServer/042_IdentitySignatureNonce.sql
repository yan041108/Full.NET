-- 042：签名认证 Nonce 防重放；高写入表使用非聚集主键与时间聚集索引。

IF OBJECT_ID(N'dbo.fn_identity_signature_nonce', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_signature_nonce
    (
        Id uniqueidentifier NOT NULL,
        AccessKeyId varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        NonceDigest char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        ExpiresAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_identity_signature_nonce PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_identity_signature_nonce_AccessKeyId
            CHECK (LEN(AccessKeyId) BETWEEN 4 AND 16),
        CONSTRAINT CK_fn_identity_signature_nonce_NonceDigest
            CHECK (LEN(NonceDigest) = 64)
    );
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证签名随机数表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_signature_nonce';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'访问密钥标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_signature_nonce', @level2type=N'COLUMN', @level2name=N'AccessKeyId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_signature_nonce', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_signature_nonce', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_signature_nonce', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'随机数摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_signature_nonce', @level2type=N'COLUMN', @level2name=N'NonceDigest';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_identity_signature_nonce')
      AND name = N'UX_fn_identity_signature_nonce_AccessKeyNonce'
)
BEGIN
    CREATE UNIQUE INDEX UX_fn_identity_signature_nonce_AccessKeyNonce
        ON dbo.fn_identity_signature_nonce(AccessKeyId, NonceDigest);
END;

IF EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_identity_signature_nonce')
      AND indexObject.name = N'UX_fn_identity_signature_nonce_AccessKeyNonce'
      AND
      (
          indexObject.is_unique = 0
          OR indexObject.has_filter = 1
          OR indexObject.is_disabled = 1
          OR
          (
              SELECT COUNT(*)
              FROM sys.index_columns AS indexColumn
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal > 0
          ) <> 2
          OR NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS indexColumn
              INNER JOIN sys.columns AS columnObject
                  ON columnObject.object_id = indexColumn.object_id
                 AND columnObject.column_id = indexColumn.column_id
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal = 1
                AND columnObject.name = N'AccessKeyId'
          )
          OR NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS indexColumn
              INNER JOIN sys.columns AS columnObject
                  ON columnObject.object_id = indexColumn.object_id
                 AND columnObject.column_id = indexColumn.column_id
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal = 2
                AND columnObject.name = N'NonceDigest'
          )
      )
)
BEGIN
    DROP INDEX UX_fn_identity_signature_nonce_AccessKeyNonce
        ON dbo.fn_identity_signature_nonce;

    CREATE UNIQUE INDEX UX_fn_identity_signature_nonce_AccessKeyNonce
        ON dbo.fn_identity_signature_nonce(AccessKeyId, NonceDigest);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_identity_signature_nonce')
      AND name = N'IX_fn_identity_signature_nonce_ExpiresAtUtc_Id'
)
BEGIN
    CREATE CLUSTERED INDEX IX_fn_identity_signature_nonce_ExpiresAtUtc_Id
        ON dbo.fn_identity_signature_nonce(ExpiresAtUtc, Id);
END;
