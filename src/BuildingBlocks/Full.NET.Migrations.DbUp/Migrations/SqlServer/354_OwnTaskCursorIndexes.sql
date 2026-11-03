-- 本人任务查询按可信租户、用户和稳定时间/Id 顺序读取；状态只用于摘要过滤。
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_import_export_task') AND name=N'IX_fn_import_export_task_Tenant_User_Created')
    CREATE INDEX IX_fn_import_export_task_Tenant_User_Created
        ON dbo.fn_import_export_task(TenantId,RequestedByUserId,CreatedAtUtc DESC,Id DESC) INCLUDE(StatusKey);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_reporting_export_task') AND name=N'IX_fn_reporting_export_task_Tenant_User_Created')
    CREATE INDEX IX_fn_reporting_export_task_Tenant_User_Created
        ON dbo.fn_reporting_export_task(TenantId,RequestedByUserId,CreatedAtUtc DESC,Id DESC) INCLUDE(StatusKey);