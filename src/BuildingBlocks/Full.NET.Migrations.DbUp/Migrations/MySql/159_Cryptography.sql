-- 159：国密密钥目录表。

CREATE TABLE IF NOT EXISTS fn_cryptography_key (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    KeyKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '密钥键',
    DisplayName varchar(200) NOT NULL COMMENT '展示名称',
    Description varchar(1000) NULL COMMENT '说明',
    Algorithm varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '算法',
    Purpose varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '用途',
    PublicKeyHex varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '公钥十六进制',
    PublicKeyFingerprint varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '公钥指纹',
    Status varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '状态',
    SortOrder int NOT NULL DEFAULT 0 COMMENT '排序',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    CONSTRAINT PK_fn_cryptography_key PRIMARY KEY (Id),
    CONSTRAINT UX_fn_cryptography_key_KeyKey UNIQUE (KeyKey),
    CONSTRAINT CK_fn_cryptography_key_Status CHECK (Status IN ('active', 'retired')),
    KEY IX_fn_cryptography_key_SortOrder (SortOrder, KeyKey)
) COMMENT='国密密钥目录表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO fn_cryptography_key
    (Id, KeyKey, DisplayName, Description, Algorithm, Purpose,
     PublicKeyHex, PublicKeyFingerprint, Status, SortOrder, CreatedAtUtc)
SELECT UUID_TO_BIN('01956000-0001-7000-8000-000000000002', 0),
       'host-integration-signing',
       'Host 集成载荷签名密钥',
       '用于受控 SM2 签名/验签首个场景的 Host 级示例密钥；私钥仅通过部署配置注入。',
       'sm2',
       'integration-payload-signature',
       'a17ff2e8117499f878cb5b81390a1f9c11384a32545463f577c674a3e233fd9b216dc58485160d62648e505b8e77d1bca31c79dfd7609caf28666d1b0454d90e',
       '6dc3bfe99bbafa5ddafa8a4c47536c2e42d9ccc5548d417d221f2d3f952737e2',
       'active',
       10,
       UTC_TIMESTAMP(6)
WHERE NOT EXISTS (
    SELECT 1 FROM fn_cryptography_key WHERE KeyKey = 'host-integration-signing'
);
