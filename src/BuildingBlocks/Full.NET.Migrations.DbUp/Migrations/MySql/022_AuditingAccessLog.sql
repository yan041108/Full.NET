-- 022：HTTP 访问审计汇总表（高写入：时间路径主查询索引）。
CREATE TABLE IF NOT EXISTS fn_auditing_access_log (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    OccurredAtUtc datetime(6) NOT NULL COMMENT '发生时间(UTC)',
    HttpMethod varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'HTTP 方法',
    RequestPath varchar(512) NOT NULL COMMENT '请求路径',
    StatusCode int NOT NULL COMMENT 'HTTP 状态码',
    DurationMs int NOT NULL COMMENT '耗时(毫秒)',
    UserId BINARY(16) NULL COMMENT '用户标识',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    TraceId varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '追踪标识',
    ClientIpFingerprint varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '客户端 IP 指纹',
    IsAuthenticated boolean NOT NULL DEFAULT false COMMENT '是否已认证',
    CONSTRAINT PK_fn_auditing_access_log PRIMARY KEY (Id),
    KEY IX_fn_auditing_access_log_OccurredAtUtc_Id (OccurredAtUtc, Id),
    KEY IX_fn_auditing_access_log_UserId_OccurredAtUtc (UserId, OccurredAtUtc)
) COMMENT='审计访问日志表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
