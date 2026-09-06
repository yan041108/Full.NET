-- 197：K3Cloud 连接配置与单据同步表。

CREATE TABLE IF NOT EXISTS fn_k3cloud_connection_config
(
    Id char(36) COLLATE utf8mb4_bin NOT NULL,
    Name varchar(128) NOT NULL,
    BaseUrl varchar(512) NOT NULL,
    AcctId varchar(64) COLLATE utf8mb4_bin NOT NULL,
    Username varchar(64) COLLATE utf8mb4_bin NOT NULL,
    PasswordProtected longtext NOT NULL,
    Lcid int NOT NULL DEFAULT 2052,
    IsDefault tinyint(1) NOT NULL DEFAULT 0,
    IsEnabled tinyint(1) NOT NULL DEFAULT 1,
    LastTestedAtUtc datetime(6) NULL,
    LastTestStatusKey varchar(32) COLLATE utf8mb4_bin NULL,
    LastTestMessage varchar(512) NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    PRIMARY KEY (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_k3cloud_document_sync
(
    Id char(36) COLLATE utf8mb4_bin NOT NULL,
    ConnectionConfigId char(36) COLLATE utf8mb4_bin NOT NULL,
    DocumentTypeKey varchar(64) COLLATE utf8mb4_bin NOT NULL,
    BusinessKey varchar(128) COLLATE utf8mb4_bin NOT NULL,
    PayloadJson longtext NOT NULL,
    StatusKey varchar(32) COLLATE utf8mb4_bin NOT NULL,
    LastStepKey varchar(16) COLLATE utf8mb4_bin NULL,
    ExternalBillId varchar(64) COLLATE utf8mb4_bin NULL,
    ExternalBillNo varchar(64) COLLATE utf8mb4_bin NULL,
    LastErrorCode varchar(64) COLLATE utf8mb4_bin NULL,
    LastErrorMessage varchar(512) NULL,
    SubmittedAtUtc datetime(6) NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    CreatedByUserId char(36) COLLATE utf8mb4_bin NOT NULL,
    Version int NOT NULL DEFAULT 1,
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_k3cloud_document_sync_StatusKey
        CHECK (StatusKey IN (
            'pending',
            'save_succeeded',
            'submitted',
            'save_failed',
            'submit_failed'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE UNIQUE INDEX UX_fn_k3cloud_document_sync_Connection_BusinessKey
    ON fn_k3cloud_document_sync (ConnectionConfigId, DocumentTypeKey, BusinessKey);

CREATE INDEX IX_fn_k3cloud_document_sync_StatusKey
    ON fn_k3cloud_document_sync (StatusKey, CreatedAtUtc, Id);
