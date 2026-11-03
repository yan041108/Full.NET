-- 保持双库结构一致；MySQL 使用 binlog 心跳，不执行 SQL Server 专用心跳更新。
CREATE TABLE IF NOT EXISTS fn_messaging_cdc_heartbeat (
    Id binary(16) NOT NULL COMMENT '固定 UUID v7 心跳身份',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '基础设施心跳更新时间',
    CONSTRAINT PK_fn_messaging_cdc_heartbeat PRIMARY KEY (Id),
    CONSTRAINT CK_fn_messaging_cdc_heartbeat_Id CHECK (Id = UUID_TO_BIN('01941f29-7c00-7000-8000-000000000001'))
) COMMENT='CDC 基础设施心跳，不承载业务事件';
INSERT IGNORE INTO fn_messaging_cdc_heartbeat (Id, UpdatedAtUtc)
VALUES (UUID_TO_BIN('01941f29-7c00-7000-8000-000000000001'), UTC_TIMESTAMP(6));
