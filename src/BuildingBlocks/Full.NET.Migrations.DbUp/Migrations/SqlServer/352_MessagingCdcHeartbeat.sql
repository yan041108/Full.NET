-- SQL Server CDC 空闲推进使用单行基础设施心跳；捕获实例由特权运维单独启用。
IF OBJECT_ID(N'dbo.fn_messaging_cdc_heartbeat', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_messaging_cdc_heartbeat (
        Id uniqueidentifier NOT NULL,
        UpdatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_messaging_cdc_heartbeat PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_messaging_cdc_heartbeat_Id CHECK (Id = '01941f29-7c00-7000-8000-000000000001')
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM dbo.fn_messaging_cdc_heartbeat WHERE Id = '01941f29-7c00-7000-8000-000000000001')
    INSERT INTO dbo.fn_messaging_cdc_heartbeat (Id, UpdatedAtUtc) VALUES ('01941f29-7c00-7000-8000-000000000001', SYSUTCDATETIME());
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_messaging_cdc_heartbeat') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'CDC 基础设施心跳，不承载业务事件', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_cdc_heartbeat';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_messaging_cdc_heartbeat') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_cdc_heartbeat'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定 UUID v7 心跳身份', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_cdc_heartbeat', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_messaging_cdc_heartbeat') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_cdc_heartbeat'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'基础设施心跳更新时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_cdc_heartbeat', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
GO
