-- 296：真实业务的有界影子采集，独立于正式 CDC 发布源。
CREATE TABLE IF NOT EXISTS fn_messaging_shadow_capture (
    Id BINARY(16) NOT NULL COMMENT '采集控制记录的应用端 UUID v7 主键',
    Generation BINARY(16) NOT NULL COMMENT '当前采集代次，重新开始时生成新 UUID v7',
    StartRequestId BINARY(16) NOT NULL COMMENT '开始采集的幂等请求键',
    MessageType VARCHAR(256) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '已登记的试点事件类型',
    SchemaVersion INT NOT NULL COMMENT '已登记的事件版本',
    State VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '采集状态 active、closed 或 blocked',
    MaxEvents INT NOT NULL COMMENT '当前代次固定的消息预算',
    MaxPayloadBytes BIGINT NOT NULL COMMENT '当前代次固定的载荷字节预算',
    CapturedCount INT NOT NULL COMMENT '与影子记录同事务确认的采集数量',
    CapturedBytes BIGINT NOT NULL COMMENT '已采集真实载荷字节总数',
    MissingMetadataCount INT NOT NULL COMMENT '采集窗口内缺少元数据的业务写入数量',
    LastReservedEventId BINARY(16) NULL COMMENT '当前事务最后预留的真实 Legacy 消息标识',
    StartedAtUtc DATETIME(6) NOT NULL COMMENT '当前代次开始时间',
    ExpiresAtUtc DATETIME(6) NOT NULL COMMENT '当前代次不可延长的截止时间',
    ClosedAtUtc DATETIME(6) NULL COMMENT '实际关闭或阻断时间',
    EndReason VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '关闭或阻断的稳定原因码',
    OperatorUserId BINARY(16) NOT NULL COMMENT '由可信认证上下文取得的操作者',
    OperatorDisplayName VARCHAR(128) NOT NULL COMMENT '操作者显示名快照',
    Reason VARCHAR(512) NOT NULL COMMENT '开始本代采集的人工理由',
    CreatedAtUtc DATETIME(6) NOT NULL COMMENT '控制记录创建时间',
    UpdatedAtUtc DATETIME(6) NOT NULL COMMENT '控制记录最近更新时间',
    StartExpectedGeneration BINARY(16) NULL COMMENT '开始请求绑定的上一代，初次为空；用于完整幂等正文核对',
    CONSTRAINT PK_fn_messaging_shadow_capture PRIMARY KEY (Id),
    CONSTRAINT UX_fn_messaging_shadow_capture_Generation UNIQUE (Generation),
    CONSTRAINT UX_fn_messaging_shadow_capture_MessageType_SchemaVersion UNIQUE (MessageType, SchemaVersion),
    CONSTRAINT CK_fn_messaging_shadow_capture_Budget CHECK (SchemaVersion > 0 AND MaxEvents BETWEEN 1 AND 10000 AND MaxPayloadBytes BETWEEN 1 AND 33554432 AND CapturedCount BETWEEN 0 AND MaxEvents AND CapturedBytes BETWEEN 0 AND MaxPayloadBytes AND MissingMetadataCount >= 0),
    CONSTRAINT CK_fn_messaging_shadow_capture_State CHECK (State IN ('active', 'closed', 'blocked'))
) COMMENT='按流唯一的受控影子采集会话' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS fn_messaging_shadow_event (
    Id BINARY(16) NOT NULL COMMENT '原 Legacy 消息的 UUID v7，影子不另行生成身份',
    CaptureGeneration BINARY(16) NOT NULL COMMENT '来源采集代次，仅供隔离影子样本',
    MessageType VARCHAR(256) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '原始业务事件类型',
    SchemaVersion INT NOT NULL COMMENT '原始业务事件版本',
    ContentType VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '原始序列化内容类型',
    TenantId BINARY(16) NULL COMMENT '原业务可信租户上下文',
    PartitionKey VARCHAR(256) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '真实业务分区键，禁止临时伪造',
    CorrelationId VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL COMMENT '原业务关联标识',
    CausationId BINARY(16) NULL COMMENT '原始因果消息标识',
    TraceParent VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '经格式校验的 W3C 链路上下文',
    Producer VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '真实业务生产者代码',
    Payload LONGBLOB NOT NULL COMMENT '原 Legacy 已序列化的同一份业务字节',
    OccurredAtUtc DATETIME(6) NOT NULL COMMENT '原 Legacy 消息发生时间',
    CONSTRAINT PK_fn_messaging_shadow_event PRIMARY KEY (Id),
    CONSTRAINT CK_fn_messaging_shadow_event_Version CHECK (SchemaVersion > 0),
    KEY IX_fn_messaging_shadow_event_OccurredAtUtc_Id (OccurredAtUtc, Id),
    KEY IX_fn_messaging_shadow_event_CaptureGeneration_Id (CaptureGeneration, Id)
) COMMENT='独立影子源；禁止作为正式业务发布源' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 固定索引的结构恢复不拼接 SQL；过程仅用于本脚本，调用后立即移除。
DROP PROCEDURE IF EXISTS fn_messaging_shadow_index_recover;
DELIMITER $$
CREATE PROCEDURE fn_messaging_shadow_index_recover()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE()
        AND TABLE_NAME = 'fn_messaging_shadow_event' AND INDEX_NAME = 'IX_fn_messaging_shadow_event_OccurredAtUtc_Id') THEN
        CREATE INDEX IX_fn_messaging_shadow_event_OccurredAtUtc_Id ON fn_messaging_shadow_event(OccurredAtUtc, Id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE()
        AND TABLE_NAME = 'fn_messaging_shadow_event' AND INDEX_NAME = 'IX_fn_messaging_shadow_event_CaptureGeneration_Id') THEN
        CREATE INDEX IX_fn_messaging_shadow_event_CaptureGeneration_Id ON fn_messaging_shadow_event(CaptureGeneration, Id);
    END IF;
END$$
DELIMITER ;
CALL fn_messaging_shadow_index_recover();
DROP PROCEDURE IF EXISTS fn_messaging_shadow_index_recover;
