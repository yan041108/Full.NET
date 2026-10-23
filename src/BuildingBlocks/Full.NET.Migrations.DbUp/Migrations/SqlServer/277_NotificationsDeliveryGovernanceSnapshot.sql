-- 277：Notifications 投递发送治理快照列。

IF COL_LENGTH(N'dbo.fn_notifications_delivery', N'ContentSnapshotJson') IS NULL
    ALTER TABLE dbo.fn_notifications_delivery ADD ContentSnapshotJson nvarchar(max) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'ContentSnapshotJson', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'按收件人冻结的语言、模板版本与内容快照(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'ContentSnapshotJson';

IF COL_LENGTH(N'dbo.fn_notifications_delivery', N'PreferenceVersion') IS NULL
    ALTER TABLE dbo.fn_notifications_delivery ADD PreferenceVersion bigint NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'PreferenceVersion', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'物化时采用的收件人渠道偏好版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'PreferenceVersion';

IF COL_LENGTH(N'dbo.fn_notifications_delivery', N'PolicyDecisionKey') IS NULL
    ALTER TABLE dbo.fn_notifications_delivery ADD PolicyDecisionKey varchar(64) COLLATE Latin1_General_100_BIN2 NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'PolicyDecisionKey', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'物化时采用的发送政策决策键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'PolicyDecisionKey';

IF COL_LENGTH(N'dbo.fn_notifications_delivery', N'SuppressionReasonCode') IS NULL
    ALTER TABLE dbo.fn_notifications_delivery ADD SuppressionReasonCode varchar(128) COLLATE Latin1_General_100_BIN2 NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'SuppressionReasonCode', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'阻止发送的稳定原因码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'SuppressionReasonCode';
