-- 199：OCR Provider 配置与身份证识别任务表。

CREATE TABLE IF NOT EXISTS fn_ocr_provider_config
(
    Id char(36) COLLATE utf8mb4_bin NOT NULL,
    ProviderKey varchar(64) COLLATE utf8mb4_bin NOT NULL,
    Name varchar(128) NOT NULL,
    BaseUrl varchar(512) NOT NULL,
    ApiKeyProtected longtext NULL,
    IsEnabled tinyint(1) NOT NULL DEFAULT 0,
    LastTestedAtUtc datetime(6) NULL,
    LastTestStatusKey varchar(32) COLLATE utf8mb4_bin NULL,
    LastTestMessage varchar(512) NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_ocr_provider_config_ProviderKey (ProviderKey)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_ocr_id_card_task
(
    Id char(36) COLLATE utf8mb4_bin NOT NULL,
    SourceFileId char(36) COLLATE utf8mb4_bin NOT NULL,
    StatusKey varchar(32) COLLATE utf8mb4_bin NOT NULL,
    RecognizedName varchar(64) NULL,
    RecognizedIdNumber varchar(32) NULL,
    RecognizedGender varchar(16) NULL,
    RecognizedNation varchar(32) NULL,
    RecognizedAddress varchar(256) NULL,
    RecognizedBirthDate varchar(32) NULL,
    ConfirmedName varchar(64) NULL,
    ConfirmedIdNumber varchar(32) NULL,
    ConfirmedGender varchar(16) NULL,
    ConfirmedNation varchar(32) NULL,
    ConfirmedAddress varchar(256) NULL,
    ConfirmedBirthDate varchar(32) NULL,
    RawResultJson longtext NULL,
    FailureMessage varchar(512) NULL,
    RecognizedAtUtc datetime(6) NULL,
    ConfirmedAtUtc datetime(6) NULL,
    RejectedAtUtc datetime(6) NULL,
    ConfirmedByUserId char(36) COLLATE utf8mb4_bin NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    CreatedByUserId char(36) COLLATE utf8mb4_bin NOT NULL,
    Version int NOT NULL DEFAULT 1,
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_ocr_id_card_task_StatusKey
        CHECK (StatusKey IN (
            'pending',
            'recognized',
            'failed',
            'confirmed',
            'rejected')),
    KEY IX_fn_ocr_id_card_task_StatusKey (StatusKey, CreatedAtUtc DESC, Id DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO fn_ocr_provider_config
    (Id, ProviderKey, Name, BaseUrl, ApiKeyProtected, IsEnabled, CreatedAtUtc, Version)
SELECT
    '018fcd80-0000-7000-8000-000000000080',
    'paddle_ocr_id_card',
    'PaddleOCR 身份证识别',
    'http://localhost:8080',
    NULL,
    0,
    UTC_TIMESTAMP(6),
    1
FROM DUAL
WHERE NOT EXISTS (
    SELECT 1 FROM fn_ocr_provider_config WHERE ProviderKey = 'paddle_ocr_id_card'
);
