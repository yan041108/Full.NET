-- 023：已认证写操作审计汇总表（高写入：时间路径主查询索引）。
CREATE TABLE IF NOT EXISTS fn_auditing_operation_log
(
    Id BINARY(16) NOT NULL,
    OccurredAtUtc datetime(6) NOT NULL,
    ActionKey varchar(256) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    HttpMethod varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    RequestPath varchar(512) NOT NULL,
    StatusCode int NOT NULL,
    DurationMs int NOT NULL,
    Succeeded boolean NOT NULL,
    UserId BINARY(16) NULL,
    TenantId BINARY(16) NULL,
    TraceId varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL,
    ClientIpFingerprint varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL,
    PermissionCode varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL,
    CONSTRAINT PK_fn_auditing_operation_log PRIMARY KEY (Id),
    KEY IX_fn_auditing_operation_log_OccurredAtUtc_Id (OccurredAtUtc, Id),
    KEY IX_fn_auditing_operation_log_UserId_OccurredAtUtc (UserId, OccurredAtUtc),
    KEY IX_fn_auditing_operation_log_ActionKey_OccurredAtUtc (ActionKey, OccurredAtUtc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
