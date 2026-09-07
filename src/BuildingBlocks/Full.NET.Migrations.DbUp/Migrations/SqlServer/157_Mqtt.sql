-- 157：MQTT 客户端目录与消息记录表。

IF OBJECT_ID(N'dbo.fn_mqtt_client', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_mqtt_client
    (
        Id uniqueidentifier NOT NULL,
        ClientKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DisplayName nvarchar(200) NOT NULL,
        Description nvarchar(1000) NULL,
        TenantId uniqueidentifier NULL,
        IsEnabled bit NOT NULL CONSTRAINT DF_fn_mqtt_client_IsEnabled DEFAULT (1),
        SortOrder int NOT NULL CONSTRAINT DF_fn_mqtt_client_SortOrder DEFAULT (0),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        CONSTRAINT PK_fn_mqtt_client PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UX_fn_mqtt_client_ClientKey UNIQUE (ClientKey)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_client')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'MQTT 客户端表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_client';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_client')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_client'), N'ClientKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'客户端键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_client', @level2type=N'COLUMN', @level2name=N'ClientKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_client')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_client'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_client', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_client')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_client'), N'Description', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'描述', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_client', @level2type=N'COLUMN', @level2name=N'Description';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_client')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_client'), N'DisplayName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_client', @level2type=N'COLUMN', @level2name=N'DisplayName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_client')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_client'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_client', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_client')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_client'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_client', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_client')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_client'), N'SortOrder', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'排序顺序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_client', @level2type=N'COLUMN', @level2name=N'SortOrder';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_client')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_client'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_client', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_client')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_client'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_client', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_client')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'MQTT 客户端目录表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_client';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_mqtt_client')
      AND indexObject.name = N'IX_fn_mqtt_client_SortOrder'
)
    CREATE INDEX IX_fn_mqtt_client_SortOrder
        ON dbo.fn_mqtt_client(SortOrder, ClientKey);

IF OBJECT_ID(N'dbo.fn_mqtt_message', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_mqtt_message
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ClientId uniqueidentifier NULL,
        Topic nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PayloadSizeBytes int NOT NULL,
        Qos int NOT NULL,
        Status varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        IdempotencyKey varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        SummaryMessage nvarchar(2000) NULL,
        PublishedAtUtc datetimeoffset(7) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CreatedByUserId uniqueidentifier NOT NULL,
        CONSTRAINT PK_fn_mqtt_message PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_mqtt_message_Client
            FOREIGN KEY (ClientId) REFERENCES dbo.fn_mqtt_client (Id),
        CONSTRAINT CK_fn_mqtt_message_Qos CHECK (Qos BETWEEN 0 AND 2),
        CONSTRAINT CK_fn_mqtt_message_Status
            CHECK (Status IN (N'pending', N'published', N'failed'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_message')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'MQTT 消息表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_message';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_message'), N'ClientId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'客户端标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_message', @level2type=N'COLUMN', @level2name=N'ClientId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_message'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_message', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_message'), N'CreatedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_message', @level2type=N'COLUMN', @level2name=N'CreatedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_message'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_message', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_message'), N'IdempotencyKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'幂等键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_message', @level2type=N'COLUMN', @level2name=N'IdempotencyKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_message'), N'PayloadSizeBytes', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'载荷大小(字节)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_message', @level2type=N'COLUMN', @level2name=N'PayloadSizeBytes';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_message'), N'PublishedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_message', @level2type=N'COLUMN', @level2name=N'PublishedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_message'), N'Qos', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'服务质量等级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_message', @level2type=N'COLUMN', @level2name=N'Qos';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_message'), N'Status', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_message', @level2type=N'COLUMN', @level2name=N'Status';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_message'), N'SummaryMessage', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'摘要消息', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_message', @level2type=N'COLUMN', @level2name=N'SummaryMessage';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_message'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_message', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_message'), N'Topic', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'主题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_message', @level2type=N'COLUMN', @level2name=N'Topic';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_mqtt_message')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'MQTT 消息发布记录表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_mqtt_message';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_mqtt_message')
      AND indexObject.name = N'IX_fn_mqtt_message_CreatedAtUtc'
)
    CREATE INDEX IX_fn_mqtt_message_CreatedAtUtc
        ON dbo.fn_mqtt_message(CreatedAtUtc DESC, Id);

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_mqtt_message')
      AND indexObject.name = N'UX_fn_mqtt_message_TenantId_IdempotencyKey'
)
    CREATE UNIQUE INDEX UX_fn_mqtt_message_TenantId_IdempotencyKey
        ON dbo.fn_mqtt_message(TenantId, IdempotencyKey)
        WHERE IdempotencyKey IS NOT NULL;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.fn_mqtt_client
    WHERE ClientKey = N'host-control-plane'
)
    INSERT INTO dbo.fn_mqtt_client
        (Id, ClientKey, DisplayName, Description, TenantId, IsEnabled, SortOrder, CreatedAtUtc)
    VALUES
        (
            '01956000-0001-7000-8000-000000000001',
            N'host-control-plane',
            N'Host 控制面客户端',
            N'用于受控发布场景验证的 Host 级 MQTT 客户端登记项。',
            NULL,
            1,
            10,
            SYSUTCDATETIME()
        );
