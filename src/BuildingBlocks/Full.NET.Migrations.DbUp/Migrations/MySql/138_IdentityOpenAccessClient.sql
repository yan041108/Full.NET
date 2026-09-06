-- 138：OpenAccess 接入方应用主表；凭据仍复用 fn_identity_api_key。

CREATE TABLE IF NOT EXISTS fn_identity_open_access_client (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    ApiKeyId BINARY(16) NOT NULL COMMENT '绑定的 API Key 标识',
    Name varchar(128) NOT NULL COMMENT '应用名称',
    Description varchar(512) NULL COMMENT '应用描述',
    Remark varchar(256) NULL COMMENT '管理员备注',
    CreatedByUserId BINARY(16) NOT NULL COMMENT '创建人用户标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_identity_open_access_client PRIMARY KEY (Id),
    CONSTRAINT UX_fn_identity_open_access_client_ApiKeyId UNIQUE (ApiKeyId),
    CONSTRAINT FK_fn_identity_open_access_client_ApiKey
        FOREIGN KEY (ApiKeyId) REFERENCES fn_identity_api_key (Id),
    KEY IX_fn_identity_open_access_client_Name (Name, CreatedAtUtc, Id)
) COMMENT='OpenAccess 接入方应用表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP PROCEDURE IF EXISTS fn_identity_open_access_client_indexes;
DELIMITER $$
CREATE PROCEDURE fn_identity_open_access_client_indexes()
BEGIN
    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_identity_open_access_client'
          AND INDEX_NAME = 'IX_fn_identity_open_access_client_Name'
    )
    THEN
        CREATE INDEX IX_fn_identity_open_access_client_Name
            ON fn_identity_open_access_client (Name, CreatedAtUtc, Id);
    END IF;
END$$
DELIMITER ;
CALL fn_identity_open_access_client_indexes();
DROP PROCEDURE fn_identity_open_access_client_indexes;
