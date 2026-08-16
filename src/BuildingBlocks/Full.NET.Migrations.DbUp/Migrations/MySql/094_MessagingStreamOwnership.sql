-- 094：事件流交付所有权持久化记录（试点切流/回退边界）。

CREATE TABLE IF NOT EXISTS fn_messaging_stream_ownership (
    MessageType varchar(128) NOT NULL COMMENT '消息类型',
    SchemaVersion int NOT NULL COMMENT 'Schema 版本',
    TopicCode varchar(128) NOT NULL COMMENT '主题编码',
    CurrentOwner tinyint NOT NULL COMMENT '当前所有者',
    PreviousOwner tinyint NOT NULL COMMENT '上一任所有者',
    CutoffEventId binary(16) NOT NULL COMMENT '截止事件标识',
    CutoffOccurredAtUtc datetime(6) NOT NULL COMMENT '截止事件发生时间(UTC)',
    CdcSourcePositionJson longtext NULL COMMENT 'CDC 源位置(JSON)',
    OperatorUserId binary(16) NULL COMMENT '操作人用户标识',
    Reason varchar(512) NOT NULL COMMENT '原因说明',
    RollbackBoundaryEventId binary(16) NULL COMMENT '回滚边界事件标识',
    RollbackOccurredAtUtc datetime(6) NULL COMMENT '回滚发生时间(UTC)',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    CONSTRAINT PK_fn_messaging_stream_ownership PRIMARY KEY (MessageType, SchemaVersion),
    CONSTRAINT CK_fn_messaging_stream_ownership_SchemaVersion CHECK (SchemaVersion BETWEEN 1 AND 65535),
    CONSTRAINT CK_fn_messaging_stream_ownership_CurrentOwner CHECK (CurrentOwner BETWEEN 0 AND 2),
    CONSTRAINT CK_fn_messaging_stream_ownership_PreviousOwner CHECK (PreviousOwner BETWEEN 0 AND 2)
) COMMENT='消息流发布所有权与回滚边界' ENGINE=InnoDB;

-- 表已存在时按契约收敛约束形状，保证重跑迁移可修复部署漂移。
ALTER TABLE fn_messaging_stream_ownership
    ADD CONSTRAINT IF NOT EXISTS CK_fn_messaging_stream_ownership_SchemaVersion CHECK (SchemaVersion BETWEEN 1 AND 65535);
ALTER TABLE fn_messaging_stream_ownership
    ADD CONSTRAINT IF NOT EXISTS CK_fn_messaging_stream_ownership_CurrentOwner CHECK (CurrentOwner BETWEEN 0 AND 2);
ALTER TABLE fn_messaging_stream_ownership
    ADD CONSTRAINT IF NOT EXISTS CK_fn_messaging_stream_ownership_PreviousOwner CHECK (PreviousOwner BETWEEN 0 AND 2);
