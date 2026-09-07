-- 197：K3Cloud 连接配置与单据同步表。

IF OBJECT_ID(N'dbo.fn_k3cloud_connection_config', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_k3cloud_connection_config
    (
        Id uniqueidentifier NOT NULL,
        Name nvarchar(128) NOT NULL,
        BaseUrl nvarchar(512) NOT NULL,
        AcctId varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Username varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PasswordProtected nvarchar(max) NOT NULL,
        Lcid int NOT NULL
            CONSTRAINT DF_fn_k3cloud_connection_config_Lcid DEFAULT (2052),
        IsDefault bit NOT NULL
            CONSTRAINT DF_fn_k3cloud_connection_config_IsDefault DEFAULT (0),
        IsEnabled bit NOT NULL
            CONSTRAINT DF_fn_k3cloud_connection_config_IsEnabled DEFAULT (1),
        LastTestedAtUtc datetimeoffset(7) NULL,
        LastTestStatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NULL,
        LastTestMessage nvarchar(512) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_k3cloud_connection_config_Version DEFAULT (1),
        CONSTRAINT PK_fn_k3cloud_connection_config PRIMARY KEY CLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_connection_config')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'金蝶云星空连接配置表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_connection_config';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_connection_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_connection_config'), N'AcctId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'账套标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_connection_config', @level2type=N'COLUMN', @level2name=N'AcctId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_connection_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_connection_config'), N'BaseUrl', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'基础地址', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_connection_config', @level2type=N'COLUMN', @level2name=N'BaseUrl';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_connection_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_connection_config'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_connection_config', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_connection_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_connection_config'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_connection_config', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_connection_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_connection_config'), N'IsDefault', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否默认', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_connection_config', @level2type=N'COLUMN', @level2name=N'IsDefault';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_connection_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_connection_config'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_connection_config', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_connection_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_connection_config'), N'LastTestMessage', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近一次探测消息', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_connection_config', @level2type=N'COLUMN', @level2name=N'LastTestMessage';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_connection_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_connection_config'), N'LastTestStatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近一次探测状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_connection_config', @level2type=N'COLUMN', @level2name=N'LastTestStatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_connection_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_connection_config'), N'LastTestedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Last Tested At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_connection_config', @level2type=N'COLUMN', @level2name=N'LastTestedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_connection_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_connection_config'), N'Lcid', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'区域语言标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_connection_config', @level2type=N'COLUMN', @level2name=N'Lcid';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_connection_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_connection_config'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_connection_config', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_connection_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_connection_config'), N'PasswordProtected', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'受保护的密码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_connection_config', @level2type=N'COLUMN', @level2name=N'PasswordProtected';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_connection_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_connection_config'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_connection_config', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_connection_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_connection_config'), N'Username', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_connection_config', @level2type=N'COLUMN', @level2name=N'Username';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_connection_config')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_connection_config'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_connection_config', @level2type=N'COLUMN', @level2name=N'Version';
END;

IF OBJECT_ID(N'dbo.fn_k3cloud_document_sync', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_k3cloud_document_sync
    (
        Id uniqueidentifier NOT NULL,
        ConnectionConfigId uniqueidentifier NOT NULL,
        DocumentTypeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        BusinessKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PayloadJson nvarchar(max) NOT NULL,
        StatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        LastStepKey varchar(16) COLLATE Latin1_General_100_BIN2 NULL,
        ExternalBillId varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        ExternalBillNo varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        LastErrorCode varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        LastErrorMessage nvarchar(512) NULL,
        SubmittedAtUtc datetimeoffset(7) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        CreatedByUserId uniqueidentifier NOT NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_k3cloud_document_sync_Version DEFAULT (1),
        CONSTRAINT PK_fn_k3cloud_document_sync PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_k3cloud_document_sync_StatusKey
            CHECK (StatusKey IN (
                N'pending',
                N'save_succeeded',
                N'submitted',
                N'save_failed',
                N'submit_failed'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'金蝶云星空单据同步表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_document_sync';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_document_sync'), N'BusinessKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'业务键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_document_sync', @level2type=N'COLUMN', @level2name=N'BusinessKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_document_sync'), N'ConnectionConfigId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'连接配置标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_document_sync', @level2type=N'COLUMN', @level2name=N'ConnectionConfigId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_document_sync'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_document_sync', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_document_sync'), N'CreatedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_document_sync', @level2type=N'COLUMN', @level2name=N'CreatedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_document_sync'), N'DocumentTypeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'单据类型键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_document_sync', @level2type=N'COLUMN', @level2name=N'DocumentTypeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_document_sync'), N'ExternalBillId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'外部单据标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_document_sync', @level2type=N'COLUMN', @level2name=N'ExternalBillId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_document_sync'), N'ExternalBillNo', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'外部单据编号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_document_sync', @level2type=N'COLUMN', @level2name=N'ExternalBillNo';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_document_sync'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_document_sync', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_document_sync'), N'LastErrorCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最后错误码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_document_sync', @level2type=N'COLUMN', @level2name=N'LastErrorCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_document_sync'), N'LastErrorMessage', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近一次错误消息', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_document_sync', @level2type=N'COLUMN', @level2name=N'LastErrorMessage';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_document_sync'), N'LastStepKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近步骤键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_document_sync', @level2type=N'COLUMN', @level2name=N'LastStepKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_document_sync'), N'PayloadJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Payload(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_document_sync', @level2type=N'COLUMN', @level2name=N'PayloadJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_document_sync'), N'StatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_document_sync', @level2type=N'COLUMN', @level2name=N'StatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_document_sync'), N'SubmittedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Submitted At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_document_sync', @level2type=N'COLUMN', @level2name=N'SubmittedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_document_sync'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_document_sync', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_k3cloud_document_sync'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_k3cloud_document_sync', @level2type=N'COLUMN', @level2name=N'Version';
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
      AND name = N'UX_fn_k3cloud_document_sync_Connection_BusinessKey')
    CREATE UNIQUE INDEX UX_fn_k3cloud_document_sync_Connection_BusinessKey
        ON dbo.fn_k3cloud_document_sync(ConnectionConfigId, DocumentTypeKey, BusinessKey);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
      AND name = N'IX_fn_k3cloud_document_sync_StatusKey')
    CREATE INDEX IX_fn_k3cloud_document_sync_StatusKey
        ON dbo.fn_k3cloud_document_sync(StatusKey, CreatedAtUtc DESC, Id DESC);
