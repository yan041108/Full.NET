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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_provider_config')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'OCR提供程序配置表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_provider_config';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_provider_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_provider_config'), N'ApiKeyProtected', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'受保护的 API 密钥', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_provider_config', @level2type=N'COLUMN', @level2name=N'ApiKeyProtected';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_provider_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_provider_config'), N'BaseUrl', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'基础地址', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_provider_config', @level2type=N'COLUMN', @level2name=N'BaseUrl';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_provider_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_provider_config'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_provider_config', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_provider_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_provider_config'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_provider_config', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_provider_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_provider_config'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_provider_config', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_provider_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_provider_config'), N'LastTestMessage', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近一次探测消息', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_provider_config', @level2type=N'COLUMN', @level2name=N'LastTestMessage';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_provider_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_provider_config'), N'LastTestStatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近一次探测状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_provider_config', @level2type=N'COLUMN', @level2name=N'LastTestStatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_provider_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_provider_config'), N'LastTestedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Last Tested At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_provider_config', @level2type=N'COLUMN', @level2name=N'LastTestedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_provider_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_provider_config'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_provider_config', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_provider_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_provider_config'), N'ProviderKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'存储提供程序键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_provider_config', @level2type=N'COLUMN', @level2name=N'ProviderKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_provider_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_provider_config'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_provider_config', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_provider_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_provider_config'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_provider_config', @level2type=N'COLUMN', @level2name=N'Version';
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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'OCR身份证识别任务表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'ConfirmedAddress', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'确认后的地址', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'ConfirmedAddress';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'ConfirmedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'确认时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'ConfirmedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'ConfirmedBirthDate', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'确认后的出生日期', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'ConfirmedBirthDate';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'ConfirmedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'确认人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'ConfirmedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'ConfirmedGender', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'确认后的性别', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'ConfirmedGender';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'ConfirmedIdNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'确认后的证件号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'ConfirmedIdNumber';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'ConfirmedName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'确认后的姓名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'ConfirmedName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'ConfirmedNation', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'确认后的民族', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'ConfirmedNation';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'CreatedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'CreatedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'FailureMessage', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'失败消息', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'FailureMessage';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'RawResultJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Raw Result(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'RawResultJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'RecognizedAddress', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'识别出的地址', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'RecognizedAddress';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'RecognizedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Recognized At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'RecognizedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'RecognizedBirthDate', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'识别出的出生日期', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'RecognizedBirthDate';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'RecognizedGender', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'识别出的性别', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'RecognizedGender';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'RecognizedIdNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'识别出的证件号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'RecognizedIdNumber';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'RecognizedName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'识别出的姓名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'RecognizedName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'RecognizedNation', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'识别出的民族', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'RecognizedNation';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'RejectedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Rejected At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'RejectedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'SourceFileId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'源文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'SourceFileId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'StatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'StatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ocr_id_card_task'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ocr_id_card_task', @level2type=N'COLUMN', @level2name=N'Version';
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
