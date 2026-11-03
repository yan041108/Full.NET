-- MySQL 的尾部状态键提供索引条件过滤；新增/状态推进各增加一棵本人查询索引的维护成本。
SET @fn_import_owner_index_sql = IF(EXISTS(SELECT 1 FROM information_schema.statistics WHERE table_schema=DATABASE()
    AND table_name='fn_import_export_task' AND index_name='IX_fn_import_export_task_Tenant_User_Created'),
    'SELECT 1', 'CREATE INDEX IX_fn_import_export_task_Tenant_User_Created ON fn_import_export_task(TenantId,RequestedByUserId,CreatedAtUtc DESC,Id DESC,StatusKey)');
PREPARE fn_import_owner_index FROM @fn_import_owner_index_sql;
EXECUTE fn_import_owner_index;
DEALLOCATE PREPARE fn_import_owner_index;
SET @fn_report_owner_index_sql = IF(EXISTS(SELECT 1 FROM information_schema.statistics WHERE table_schema=DATABASE()
    AND table_name='fn_reporting_export_task' AND index_name='IX_fn_reporting_export_task_Tenant_User_Created'),
    'SELECT 1', 'CREATE INDEX IX_fn_reporting_export_task_Tenant_User_Created ON fn_reporting_export_task(TenantId,RequestedByUserId,CreatedAtUtc DESC,Id DESC,StatusKey)');
PREPARE fn_report_owner_index FROM @fn_report_owner_index_sql;
EXECUTE fn_report_owner_index;
DEALLOCATE PREPARE fn_report_owner_index;