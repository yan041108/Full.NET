-- 按 ChallengeId 保存独立投递结果，不复制凭据或载荷。增量可空列保留旧挑战原有效期。
-- 保障要求全部消费实例升级；混跑旧消费代码或回退前必须停止挑战入口并排空有效新挑战。
IF COL_LENGTH(N'dbo.fn_identity_account_challenge', N'DeliveryStateKey') IS NULL
    ALTER TABLE dbo.fn_identity_account_challenge ADD DeliveryStateKey varchar(16) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.fn_identity_account_challenge')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_account_challenge'), N'DeliveryStateKey', 'ColumnId')
      AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'投递结果机器码；空值仅表示迁移前旧挑战',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_account_challenge',
        @level2type=N'COLUMN', @level2name=N'DeliveryStateKey';

IF COL_LENGTH(N'dbo.fn_identity_account_challenge', N'DeliveryCompletedAtUtc') IS NULL
    ALTER TABLE dbo.fn_identity_account_challenge ADD DeliveryCompletedAtUtc datetimeoffset(7) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.fn_identity_account_challenge')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_account_challenge'), N'DeliveryCompletedAtUtc', 'ColumnId')
      AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'投递结果持久化时间(UTC)',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_account_challenge',
        @level2type=N'COLUMN', @level2name=N'DeliveryCompletedAtUtc';

IF COL_LENGTH(N'dbo.fn_identity_account_challenge', N'DeliveryReconciledAtUtc') IS NULL
    ALTER TABLE dbo.fn_identity_account_challenge ADD DeliveryReconciledAtUtc datetimeoffset(7) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.fn_identity_account_challenge')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_account_challenge'), N'DeliveryReconciledAtUtc', 'ColumnId')
      AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'未确认投递撤销对账时间(UTC)',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_account_challenge',
        @level2type=N'COLUMN', @level2name=N'DeliveryReconciledAtUtc';
