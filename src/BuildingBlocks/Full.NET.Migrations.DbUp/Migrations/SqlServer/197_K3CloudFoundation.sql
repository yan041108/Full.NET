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
