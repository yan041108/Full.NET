-- 为 Outbox 引入死信终态，区分可重试失败与需人工介入的毒消息。
-- SQL Server：使用 COL_LENGTH 守卫列追加，保证 DbUp 未记账但部分 DDL 已提交时可安全重跑。

IF COL_LENGTH(N'dbo.fn_outbox_message', N'DeadLetteredAtUtc') IS NULL
    ALTER TABLE dbo.fn_outbox_message
        ADD DeadLetteredAtUtc datetimeoffset(7) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'DeadLetteredAtUtc', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'死信时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'DeadLetteredAtUtc';

IF COL_LENGTH(N'dbo.fn_outbox_message', N'DeadLetterReasonCode') IS NULL
    ALTER TABLE dbo.fn_outbox_message
        ADD DeadLetterReasonCode nvarchar(128) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'DeadLetterReasonCode', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'死信原因码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'DeadLetterReasonCode';
