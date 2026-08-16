-- 024：未处理异常审计汇总表（高写入：时间路径主查询索引）。
CREATE TABLE IF NOT EXISTS fn_auditing_exception_log (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    OccurredAtUtc datetime(6) NOT NULL COMMENT '发生时间(UTC)',
    ExceptionType varchar(256) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '异常类型',
    Message varchar(1024) NOT NULL COMMENT '消息文本',
    StackTrace varchar(4000) NULL COMMENT '堆栈跟踪',
    HttpMethod varchar(16) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT 'HTTP 方法',
    RequestPath varchar(512) NULL COMMENT '请求路径',
    UserId BINARY(16) NULL COMMENT '用户标识',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    TraceId varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '追踪标识',
    ClientIpFingerprint varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '客户端 IP 指纹',
    CONSTRAINT PK_fn_auditing_exception_log PRIMARY KEY (Id),
    KEY IX_fn_auditing_exception_log_OccurredAtUtc_Id (OccurredAtUtc, Id),
    KEY IX_fn_auditing_exception_log_ExceptionType_OccurredAtUtc (ExceptionType, OccurredAtUtc)
) COMMENT='审计异常日志表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
