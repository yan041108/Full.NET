-- 199：OCR Provider 配置与身份证识别任务表。

IF OBJECT_ID(N'dbo.fn_ocr_provider_config', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ocr_provider_config
    (
        Id uniqueidentifier NOT NULL,
        ProviderKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Name nvarchar(128) NOT NULL,
        BaseUrl nvarchar(512) NOT NULL,
        ApiKeyProtected nvarchar(max) NULL,
        IsEnabled bit NOT NULL
            CONSTRAINT DF_fn_ocr_provider_config_IsEnabled DEFAULT (0),
        LastTestedAtUtc datetimeoffset(7) NULL,
        LastTestStatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NULL,
        LastTestMessage nvarchar(512) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_ocr_provider_config_Version DEFAULT (1),
        CONSTRAINT PK_fn_ocr_provider_config PRIMARY KEY CLUSTERED (Id)
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_ocr_provider_config')
      AND name = N'UX_fn_ocr_provider_config_ProviderKey')
    CREATE UNIQUE INDEX UX_fn_ocr_provider_config_ProviderKey
        ON dbo.fn_ocr_provider_config(ProviderKey);

IF OBJECT_ID(N'dbo.fn_ocr_id_card_task', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ocr_id_card_task
    (
        Id uniqueidentifier NOT NULL,
        SourceFileId uniqueidentifier NOT NULL,
        StatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        RecognizedName nvarchar(64) NULL,
        RecognizedIdNumber nvarchar(32) NULL,
        RecognizedGender nvarchar(16) NULL,
        RecognizedNation nvarchar(32) NULL,
        RecognizedAddress nvarchar(256) NULL,
        RecognizedBirthDate nvarchar(32) NULL,
        ConfirmedName nvarchar(64) NULL,
        ConfirmedIdNumber nvarchar(32) NULL,
        ConfirmedGender nvarchar(16) NULL,
        ConfirmedNation nvarchar(32) NULL,
        ConfirmedAddress nvarchar(256) NULL,
        ConfirmedBirthDate nvarchar(32) NULL,
        RawResultJson nvarchar(max) NULL,
        FailureMessage nvarchar(512) NULL,
        RecognizedAtUtc datetimeoffset(7) NULL,
        ConfirmedAtUtc datetimeoffset(7) NULL,
        RejectedAtUtc datetimeoffset(7) NULL,
        ConfirmedByUserId uniqueidentifier NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        CreatedByUserId uniqueidentifier NOT NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_ocr_id_card_task_Version DEFAULT (1),
        CONSTRAINT PK_fn_ocr_id_card_task PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_ocr_id_card_task_StatusKey
            CHECK (StatusKey IN (
                N'pending',
                N'recognized',
                N'failed',
                N'confirmed',
                N'rejected'))
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
      AND name = N'IX_fn_ocr_id_card_task_StatusKey')
    CREATE INDEX IX_fn_ocr_id_card_task_StatusKey
        ON dbo.fn_ocr_id_card_task(StatusKey, CreatedAtUtc DESC, Id DESC);

IF NOT EXISTS (
    SELECT 1 FROM dbo.fn_ocr_provider_config
    WHERE ProviderKey = N'paddle_ocr_id_card')
BEGIN
    INSERT INTO dbo.fn_ocr_provider_config
        (Id, ProviderKey, Name, BaseUrl, ApiKeyProtected, IsEnabled, CreatedAtUtc, Version)
    VALUES
        (
            '018fcd80-0000-7000-8000-000000000080',
            N'paddle_ocr_id_card',
            N'PaddleOCR 身份证识别',
            N'http://localhost:8080',
            NULL,
            0,
            SYSUTCDATETIME(),
            1
        );
END;
