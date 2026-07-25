-- 022：HTTP 访问审计汇总表（高写入：时间路径主查询索引）。
CREATE TABLE IF NOT EXISTS fn_auditing_access_log
(
    Id BINARY(16) NOT NULL,
    OccurredAtUtc datetime(6) NOT NULL,
    HttpMethod varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    RequestPath varchar(512) NOT NULL,
    StatusCode int NOT NULL,
    DurationMs int NOT NULL,
    UserId BINARY(16) NULL,
    TenantId BINARY(16) NULL,
    TraceId varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL,
    ClientIpFingerprint varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL,
    IsAuthenticated boolean NOT NULL DEFAULT false,
    CONSTRAINT PK_fn_auditing_access_log PRIMARY KEY (Id),
    KEY IX_fn_auditing_access_log_OccurredAtUtc_Id (OccurredAtUtc, Id),
    KEY IX_fn_auditing_access_log_UserId_OccurredAtUtc (UserId, OccurredAtUtc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
