-- 157：MQTT 客户端目录与消息记录表。

CREATE TABLE IF NOT EXISTS fn_mqtt_client (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    ClientKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '客户端键',
    DisplayName varchar(200) NOT NULL COMMENT '展示名称',
    Description varchar(1000) NULL COMMENT '说明',
    TenantId BINARY(16) NULL COMMENT '租户标识',
    IsEnabled tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    SortOrder int NOT NULL DEFAULT 0 COMMENT '排序',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    CONSTRAINT PK_fn_mqtt_client PRIMARY KEY (Id),
    CONSTRAINT UX_fn_mqtt_client_ClientKey UNIQUE (ClientKey),
    KEY IX_fn_mqtt_client_SortOrder (SortOrder, ClientKey)
) COMMENT='MQTT 客户端目录表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_mqtt_message (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识',
    ClientId BINARY(16) NULL COMMENT '客户端标识',
    Topic varchar(256) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '主题',
    PayloadSizeBytes int NOT NULL COMMENT '载荷大小(字节)',
    Qos int NOT NULL COMMENT 'QoS',
    Status varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '状态',
    IdempotencyKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '幂等键',
    SummaryMessage varchar(2000) NULL COMMENT '摘要或错误',
    PublishedAtUtc datetime(6) NULL COMMENT '发布时间(UTC)',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CreatedByUserId BINARY(16) NOT NULL COMMENT '创建人',
    CONSTRAINT PK_fn_mqtt_message PRIMARY KEY (Id),
    CONSTRAINT FK_fn_mqtt_message_Client
        FOREIGN KEY (ClientId) REFERENCES fn_mqtt_client (Id),
    CONSTRAINT CK_fn_mqtt_message_Qos CHECK (Qos BETWEEN 0 AND 2),
    CONSTRAINT CK_fn_mqtt_message_Status
        CHECK (Status IN ('pending', 'published', 'failed')),
    KEY IX_fn_mqtt_message_CreatedAtUtc (CreatedAtUtc DESC, Id)
) COMMENT='MQTT 消息发布记录表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP PROCEDURE IF EXISTS fn_mqtt_message_indexes;
DELIMITER $$
CREATE PROCEDURE fn_mqtt_message_indexes()
BEGIN
    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_mqtt_message'
          AND INDEX_NAME = 'UX_fn_mqtt_message_TenantId_IdempotencyKey'
    )
    THEN
        CREATE UNIQUE INDEX UX_fn_mqtt_message_TenantId_IdempotencyKey
            ON fn_mqtt_message (TenantId, IdempotencyKey);
    END IF;
END$$
DELIMITER ;
CALL fn_mqtt_message_indexes();
DROP PROCEDURE fn_mqtt_message_indexes;

INSERT INTO fn_mqtt_client
    (Id, ClientKey, DisplayName, Description, TenantId, IsEnabled, SortOrder, CreatedAtUtc)
SELECT UUID_TO_BIN('01956000-0001-7000-8000-000000000001', 0),
       'host-control-plane',
       'Host 控制面客户端',
       '用于受控发布场景验证的 Host 级 MQTT 客户端登记项。',
       NULL,
       1,
       10,
       UTC_TIMESTAMP(6)
WHERE NOT EXISTS (
    SELECT 1 FROM fn_mqtt_client WHERE ClientKey = 'host-control-plane'
);
