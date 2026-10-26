-- 建表、约束和索引由同一 IF NOT EXISTS 保护。
CREATE TABLE IF NOT EXISTS fn_messaging_cutover_evidence
(
    Id BINARY(16) NOT NULL COMMENT '应用生成的切流证明 UUID v7',
    EventType varchar(256) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '证明所属事件流的稳定消息类型',
    SchemaVersion int NOT NULL COMMENT '事件流结构版本',
    ResourceFingerprint varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '环境配置与真实目标实例的 SHA256 摘要',
    SourceDigest varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '权威样本完整摘要',
    ShadowPositionDigest varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '完整影子扫描的固定起止位点摘要',
    SampleCount int NOT NULL COMMENT '已完整核对的样本数量',
    VerifiedAtUtc datetime(6) NOT NULL COMMENT '验证成功的 UTC 时间',
    ExpiresAtUtc datetime(6) NOT NULL COMMENT '证明失效的 UTC 时间',
    OperatorUserId BINARY(16) NOT NULL COMMENT '可信验证操作者标识',
    Reason varchar(512) NOT NULL COMMENT '请求生成证明的不可变运维理由',
    CONSTRAINT PK_fn_messaging_cutover_evidence PRIMARY KEY (Id),
    KEY IX_fn_messaging_cutover_evidence_VerifiedAtUtc_Id (VerifiedAtUtc, Id),
    CONSTRAINT CK_fn_messaging_cutover_evidence_SampleCount CHECK (SampleCount BETWEEN 1 AND 10000)
) COMMENT='切流影子验证与真实目标链路的短期证明' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
