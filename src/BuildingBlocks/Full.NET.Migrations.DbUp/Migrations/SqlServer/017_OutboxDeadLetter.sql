-- 为 Outbox 引入死信终态，区分可重试失败与需人工介入的毒消息。
-- SQL Server：使用 COL_LENGTH 守卫列追加，保证 DbUp 未记账但部分 DDL 已提交时可安全重跑。

IF COL_LENGTH(N'dbo.fn_outbox_message', N'DeadLetteredAtUtc') IS NULL
    ALTER TABLE dbo.fn_outbox_message
        ADD DeadLetteredAtUtc datetimeoffset(7) NULL;

IF COL_LENGTH(N'dbo.fn_outbox_message', N'DeadLetterReasonCode') IS NULL
    ALTER TABLE dbo.fn_outbox_message
        ADD DeadLetterReasonCode nvarchar(128) NULL;
