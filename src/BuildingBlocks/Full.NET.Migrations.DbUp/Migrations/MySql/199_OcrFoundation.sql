-- 199：OCR Provider 配置与身份证识别任务表。

CREATE TABLE IF NOT EXISTS fn_ocr_provider_config (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    ProviderKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '存储提供程序键',
    Name varchar(128) NOT NULL COMMENT '名称',
    BaseUrl varchar(512) NOT NULL COMMENT '基础地址',
    ApiKeyProtected longtext NULL COMMENT '受保护的 API 密钥',
    IsEnabled tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否启用',
    LastTestedAtUtc datetime(6) NULL COMMENT 'Last Tested At(UTC)',
    LastTestStatusKey varchar(32) COLLATE utf8mb4_bin NULL COMMENT '最近一次探测状态键',
    LastTestMessage varchar(512) NULL COMMENT '最近一次探测消息',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_ocr_provider_config_ProviderKey (ProviderKey)
) COMMENT='OCR提供程序配置表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_ocr_id_card_task (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    SourceFileId BINARY(16) NOT NULL COMMENT '源文件标识',
    StatusKey varchar(32) COLLATE utf8mb4_bin NOT NULL COMMENT '状态键',
    RecognizedName varchar(64) NULL COMMENT '识别出的姓名',
    RecognizedIdNumber varchar(32) NULL COMMENT '识别出的证件号',
    RecognizedGender varchar(16) NULL COMMENT '识别出的性别',
    RecognizedNation varchar(32) NULL COMMENT '识别出的民族',
    RecognizedAddress varchar(256) NULL COMMENT '识别出的地址',
    RecognizedBirthDate varchar(32) NULL COMMENT '识别出的出生日期',
    ConfirmedName varchar(64) NULL COMMENT '确认后的姓名',
    ConfirmedIdNumber varchar(32) NULL COMMENT '确认后的证件号',
    ConfirmedGender varchar(16) NULL COMMENT '确认后的性别',
    ConfirmedNation varchar(32) NULL COMMENT '确认后的民族',
    ConfirmedAddress varchar(256) NULL COMMENT '确认后的地址',
    ConfirmedBirthDate varchar(32) NULL COMMENT '确认后的出生日期',
    RawResultJson longtext NULL COMMENT 'Raw Result(JSON)',
    FailureMessage varchar(512) NULL COMMENT '失败消息',
    RecognizedAtUtc datetime(6) NULL COMMENT 'Recognized At(UTC)',
    ConfirmedAtUtc datetime(6) NULL COMMENT '确认时间(UTC)',
    RejectedAtUtc datetime(6) NULL COMMENT 'Rejected At(UTC)',
    ConfirmedByUserId BINARY(16) NULL COMMENT '确认人用户标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    CreatedByUserId BINARY(16) NOT NULL COMMENT '创建人用户标识',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_ocr_id_card_task_StatusKey
        CHECK (StatusKey IN (
            'pending',
            'recognized',
            'failed',
            'confirmed',
            'rejected')),
    KEY IX_fn_ocr_id_card_task_StatusKey (StatusKey, CreatedAtUtc DESC, Id DESC)
) COMMENT='OCR身份证识别任务表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO fn_ocr_provider_config
    (Id, ProviderKey, Name, BaseUrl, ApiKeyProtected, IsEnabled, CreatedAtUtc, Version)
SELECT
    UUID_TO_BIN('018fcd80-0000-7000-8000-000000000080', 0),
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
