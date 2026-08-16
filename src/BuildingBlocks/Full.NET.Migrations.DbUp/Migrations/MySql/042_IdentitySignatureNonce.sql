-- 042：签名认证 Nonce 防重放。

CREATE TABLE IF NOT EXISTS fn_identity_signature_nonce (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    AccessKeyId varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '访问密钥标识',
    NonceDigest char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '随机数摘要',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    ExpiresAtUtc datetime(6) NOT NULL COMMENT '过期时间(UTC)',
    CONSTRAINT PK_fn_identity_signature_nonce PRIMARY KEY (Id),
    CONSTRAINT CK_fn_identity_signature_nonce_AccessKeyId
        CHECK (CHAR_LENGTH(AccessKeyId) BETWEEN 4 AND 16),
    CONSTRAINT CK_fn_identity_signature_nonce_NonceDigest
        CHECK (CHAR_LENGTH(NonceDigest) = 64),
    UNIQUE KEY UX_fn_identity_signature_nonce_AccessKeyNonce (AccessKeyId, NonceDigest),
    KEY IX_fn_identity_signature_nonce_ExpiresAtUtc_Id (ExpiresAtUtc, Id)
) COMMENT='身份认证签名随机数表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

SET @index_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_identity_signature_nonce'
      AND INDEX_NAME = 'UX_fn_identity_signature_nonce_AccessKeyNonce'
      AND NON_UNIQUE = 0
      AND ((SEQ_IN_INDEX = 1 AND COLUMN_NAME = 'AccessKeyId')
           OR (SEQ_IN_INDEX = 2 AND COLUMN_NAME = 'NonceDigest'))
);
SET @index_column_count := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_identity_signature_nonce'
      AND INDEX_NAME = 'UX_fn_identity_signature_nonce_AccessKeyNonce'
);
SET @ddl := IF(
    @index_exists = 2 AND @index_column_count = 2,
    'SELECT 1',
    'ALTER TABLE fn_identity_signature_nonce DROP INDEX UX_fn_identity_signature_nonce_AccessKeyNonce');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @ddl := IF(
    @index_exists = 2 AND @index_column_count = 2,
    'SELECT 1',
    'CREATE UNIQUE INDEX UX_fn_identity_signature_nonce_AccessKeyNonce ON fn_identity_signature_nonce (AccessKeyId, NonceDigest)');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
