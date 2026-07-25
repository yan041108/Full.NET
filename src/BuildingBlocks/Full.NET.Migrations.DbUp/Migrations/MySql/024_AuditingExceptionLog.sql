-- 024：未处理异常审计汇总表（高写入：时间路径主查询索引）。
CREATE TABLE IF NOT EXISTS fn_auditing_exception_log
(
    Id BINARY(16) NOT NULL,
    OccurredAtUtc datetime(6) NOT NULL,
    ExceptionType varchar(256) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    Message varchar(1024) NOT NULL,
    StackTrace varchar(4000) NULL,
    HttpMethod varchar(16) CHARACTER SET ascii COLLATE ascii_bin NULL,
    RequestPath varchar(512) NULL,
    UserId BINARY(16) NULL,
    TenantId BINARY(16) NULL,
    TraceId varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL,
    ClientIpFingerprint varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL,
    CONSTRAINT PK_fn_auditing_exception_log PRIMARY KEY (Id),
    KEY IX_fn_auditing_exception_log_OccurredAtUtc_Id (OccurredAtUtc, Id),
    KEY IX_fn_auditing_exception_log_ExceptionType_OccurredAtUtc (ExceptionType, OccurredAtUtc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
