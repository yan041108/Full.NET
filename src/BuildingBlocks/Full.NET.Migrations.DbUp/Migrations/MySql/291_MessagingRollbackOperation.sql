-- 单独建表与索引均受 IF NOT EXISTS 保护，DbUp 未记账时可重跑。
CREATE TABLE IF NOT EXISTS fn_messaging_rollback_operation
(
    Id BINARY(16) NOT NULL COMMENT '应用生成的回退代次 UUID v7',
    EventType varchar(256) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '操作所属事件流的稳定消息类型',
    SchemaVersion int NOT NULL COMMENT '事件流结构版本，必须大于零',
    Phase int NOT NULL COMMENT '持久回退状态，包含外部动作意图与最终结果',
    ResourcesJson longtext NULL COMMENT '暂停前冻结的控制资源快照，禁止向客户端返回',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '操作创建的 UTC 时间',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '最近一次状态迁移的 UTC 时间',
    HasPauseIntent boolean NOT NULL DEFAULT 0 COMMENT '曾经持久化暂停意图，恢复时不可丢失',
    OperatorUserId BINARY(16) NULL COMMENT '建立回退操作的可信操作者标识',
    Reason varchar(512) NULL COMMENT '建立回退操作的不可变运维理由',
    CONSTRAINT PK_fn_messaging_rollback_operation PRIMARY KEY (Id),
    KEY IX_fn_messaging_rollback_operation_CreatedAtUtc_Id (CreatedAtUtc, Id),
    CONSTRAINT CK_fn_messaging_rollback_operation_Phase CHECK (Phase BETWEEN 0 AND 5),
    CONSTRAINT CK_fn_messaging_rollback_operation_SchemaVersion CHECK (SchemaVersion > 0)
) COMMENT='事件交付回退操作与控制面恢复日志' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
