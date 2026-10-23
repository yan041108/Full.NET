-- 将个人归档、业务撤回与外发边界分开，支持半完成迁移重跑。
IF COL_LENGTH(N'dbo.fn_notifications_delivery', N'DispatchLeaseGeneration') IS NULL
    ALTER TABLE dbo.fn_notifications_delivery ADD DispatchLeaseGeneration bigint NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'DispatchLeaseGeneration', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Dispatch Lease Generation', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'DispatchLeaseGeneration';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_notifications_delivery') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'DispatchLeaseGeneration', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近一次越过外发边界的租约世代，不证明厂商已调用成功', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'DispatchLeaseGeneration';
GO
IF COL_LENGTH(N'dbo.fn_notifications_inbox_message', N'ArchivedAtUtc') IS NULL
    ALTER TABLE dbo.fn_notifications_inbox_message ADD ArchivedAtUtc datetimeoffset(7) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'ArchivedAtUtc', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Archived At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'ArchivedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'ArchivedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'个人归档时间（UTC），不改变已读状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'ArchivedAtUtc';
GO
IF COL_LENGTH(N'dbo.fn_notifications_inbox_message', N'RevokedAtUtc') IS NULL
    ALTER TABLE dbo.fn_notifications_inbox_message ADD RevokedAtUtc datetimeoffset(7) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'RevokedAtUtc', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'撤销时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'RevokedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'RevokedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'业务撤回时间（UTC），不承诺收回外发消息', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'RevokedAtUtc';
GO
IF COL_LENGTH(N'dbo.fn_notifications_inbox_message', N'RevocationReason') IS NULL
    ALTER TABLE dbo.fn_notifications_inbox_message ADD RevocationReason nvarchar(128) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'RevocationReason', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Revocation Reason', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'RevocationReason';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'RevocationReason', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'业务撤回的审计原因', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'RevocationReason';
GO
IF COL_LENGTH(N'dbo.fn_notifications_inbox_message', N'DeepLinkJson') IS NULL
    ALTER TABLE dbo.fn_notifications_inbox_message ADD DeepLinkJson nvarchar(512) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'DeepLinkJson', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Deep Link(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'DeepLinkJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'DeepLinkJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'登录后受控资源引用，不构成访问授权', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'DeepLinkJson';
GO
IF COL_LENGTH(N'dbo.fn_notifications_inbox_message', N'ProducerKey') IS NULL
    ALTER TABLE dbo.fn_notifications_inbox_message ADD ProducerKey nvarchar(128) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'ProducerKey', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'业务生产者键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'ProducerKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'ProducerKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'生成通知的业务生产者稳定键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'ProducerKey';
GO
IF COL_LENGTH(N'dbo.fn_notifications_inbox_message', N'SceneKey') IS NULL
    ALTER TABLE dbo.fn_notifications_inbox_message ADD SceneKey nvarchar(128) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'SceneKey', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'业务场景键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'SceneKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'SceneKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'业务场景稳定键，用于收件箱筛选', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'SceneKey';
GO

-- 原候选数组直接演进为元数据对象，候选内容保持不变；重跑只处理尚未转换的数组。
UPDATE dbo.fn_notifications_intent
SET RouteSnapshotJson = N'{"targets":' + RouteSnapshotJson + N'}'
WHERE ISJSON(RouteSnapshotJson, ARRAY) = 1;
GO

-- 补偿按状态与接收顺序有界读取；消息号索引避免每轮扫描全部 Attempt。
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_notifications_receipt') AND name = N'IX_fn_notifications_receipt_ProcessStatusKey')
    CREATE INDEX IX_fn_notifications_receipt_ProcessStatusKey ON dbo.fn_notifications_receipt(ProcessStatusKey, ReceivedAtUtc, Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_notifications_delivery_attempt') AND name = N'IX_fn_notifications_delivery_attempt_ProviderMessageId')
    CREATE INDEX IX_fn_notifications_delivery_attempt_ProviderMessageId ON dbo.fn_notifications_delivery_attempt(ProviderMessageId) INCLUDE (DeliveryId);
GO
