-- 207：导入/报表任务执行租约。崩溃后必须能按到期租约重新领取，禁止把进行中的任务永久卡死。
-- SQL Server 以 COL_LENGTH 幂等加列；CHECK 以 DROP/ADD 扩展报表 queued 状态。升级时停止旧 API/Worker。

IF COL_LENGTH(N'dbo.fn_import_export_task', N'LeaseId') IS NULL
    ALTER TABLE dbo.fn_import_export_task ADD LeaseId uniqueidentifier NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'LeaseId', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'LeaseId';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.fn_import_export_task')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'LeaseId', 'ColumnId')
      AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前执行租约标识',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task',
        @level2type=N'COLUMN', @level2name=N'LeaseId';

IF COL_LENGTH(N'dbo.fn_import_export_task', N'LeaseExpiresAtUtc') IS NULL
    ALTER TABLE dbo.fn_import_export_task ADD LeaseExpiresAtUtc datetimeoffset(7) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'LeaseExpiresAtUtc', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'LeaseExpiresAtUtc';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.fn_import_export_task')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'LeaseExpiresAtUtc', 'ColumnId')
      AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'执行租约到期时间 UTC',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task',
        @level2type=N'COLUMN', @level2name=N'LeaseExpiresAtUtc';

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_import_export_task')
      AND name = N'IX_fn_import_export_task_TenantId_StatusKey_LeaseExpiresAtUtc')
    CREATE INDEX IX_fn_import_export_task_TenantId_StatusKey_LeaseExpiresAtUtc
        ON dbo.fn_import_export_task (TenantId, StatusKey, LeaseExpiresAtUtc, CreatedAtUtc, Id);

IF COL_LENGTH(N'dbo.fn_reporting_export_task', N'LeaseId') IS NULL
    ALTER TABLE dbo.fn_reporting_export_task ADD LeaseId uniqueidentifier NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'LeaseId', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'LeaseId';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'LeaseId', 'ColumnId')
      AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前执行租约标识',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task',
        @level2type=N'COLUMN', @level2name=N'LeaseId';

IF COL_LENGTH(N'dbo.fn_reporting_export_task', N'LeaseExpiresAtUtc') IS NULL
    ALTER TABLE dbo.fn_reporting_export_task ADD LeaseExpiresAtUtc datetimeoffset(7) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'LeaseExpiresAtUtc', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'LeaseExpiresAtUtc';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'LeaseExpiresAtUtc', 'ColumnId')
      AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'执行租约到期时间 UTC',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task',
        @level2type=N'COLUMN', @level2name=N'LeaseExpiresAtUtc';

IF COL_LENGTH(N'dbo.fn_reporting_export_task', N'ActorPermissionCodesJson') IS NULL
    ALTER TABLE dbo.fn_reporting_export_task ADD ActorPermissionCodesJson nvarchar(max) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'ActorPermissionCodesJson', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Actor Permission Codes(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'ActorPermissionCodesJson';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'ActorPermissionCodesJson', 'ColumnId')
      AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时主体权限码快照 JSON，供 Worker 崩溃恢复重建授权',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task',
        @level2type=N'COLUMN', @level2name=N'ActorPermissionCodesJson';

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
      AND name = N'IX_fn_reporting_export_task_TenantId_StatusKey_LeaseExpiresAtUtc')
    CREATE INDEX IX_fn_reporting_export_task_TenantId_StatusKey_LeaseExpiresAtUtc
        ON dbo.fn_reporting_export_task (TenantId, StatusKey, LeaseExpiresAtUtc, CreatedAtUtc, Id);

IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_reporting_export_task_StatusKey'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_reporting_export_task'))
    ALTER TABLE dbo.fn_reporting_export_task DROP CONSTRAINT CK_fn_reporting_export_task_StatusKey;

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_reporting_export_task_StatusKey'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_reporting_export_task'))
    ALTER TABLE dbo.fn_reporting_export_task
        ADD CONSTRAINT CK_fn_reporting_export_task_StatusKey
            CHECK (StatusKey IN (N'queued', N'processing', N'succeeded', N'failed'));
