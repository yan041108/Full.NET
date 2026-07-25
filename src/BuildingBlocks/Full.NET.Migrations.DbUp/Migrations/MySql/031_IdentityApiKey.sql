-- 031：Host 作用域 API Key 凭据（仅保存哈希）。

CREATE TABLE IF NOT EXISTS fn_identity_api_key
(
    Id BINARY(16) NOT NULL,
    UserId BINARY(16) NOT NULL,
    DisplayName varchar(128) NOT NULL,
    KeyPrefix varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    KeyHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    PermissionsJson varchar(4000) NOT NULL,
    ExpiresAtUtc datetime(6) NULL,
    IsActive tinyint(1) NOT NULL,
    LastUsedAtUtc datetime(6) NULL,
    DisabledAtUtc datetime(6) NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    CONSTRAINT PK_fn_identity_api_key PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_identity_api_key_KeyHash (KeyHash),
    KEY IX_fn_identity_api_key_UserCreatedAtUtc (UserId, CreatedAtUtc, Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
