-- 043：出站调用审计汇总表。

CREATE TABLE IF NOT EXISTS fn_auditing_outbound_call
(
    Id BINARY(16) NOT NULL,
    OccurredAtUtc datetime(6) NOT NULL,
    ProviderKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    OperationKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    DestinationHostCategory varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    StatusCode int NOT NULL,
    Succeeded tinyint(1) NOT NULL DEFAULT 0,
    DurationMs int NOT NULL,
    RetryCount int NOT NULL DEFAULT 0,
    TraceId varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL,
    SafeErrorCode varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL,
    TenantId BINARY(16) NULL,
    UserId BINARY(16) NULL,
    CONSTRAINT PK_fn_auditing_outbound_call PRIMARY KEY (Id),
    CONSTRAINT CK_fn_auditing_outbound_call_StatusCode
        CHECK (StatusCode BETWEEN 0 AND 999),
    CONSTRAINT CK_fn_auditing_outbound_call_DurationMs
        CHECK (DurationMs >= 0),
    CONSTRAINT CK_fn_auditing_outbound_call_RetryCount
        CHECK (RetryCount >= 0),
    KEY IX_fn_auditing_outbound_call_OccurredAtUtc_Id (OccurredAtUtc, Id),
    KEY IX_fn_auditing_outbound_call_ProviderKey_OccurredAtUtc (ProviderKey, OccurredAtUtc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
