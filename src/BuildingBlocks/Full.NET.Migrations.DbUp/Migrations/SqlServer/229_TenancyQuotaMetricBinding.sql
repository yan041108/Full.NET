-- 229：新预留绑定实际配额记录；历史归属没有可靠证据，保留 NULL 等待对账。
IF COL_LENGTH(N'dbo.fn_tenancy_quota_reservation', N'MetricId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_tenancy_quota_reservation ADD MetricId uniqueidentifier NULL;
END;
-- 列已创建但尚未记账时，重跑也要补齐说明。
IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_tenancy_quota_reservation')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_quota_reservation'), N'MetricId', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际配额记录标识；NULL 表示历史归属待对账',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_quota_reservation',
        @level2type=N'COLUMN', @level2name=N'MetricId';
