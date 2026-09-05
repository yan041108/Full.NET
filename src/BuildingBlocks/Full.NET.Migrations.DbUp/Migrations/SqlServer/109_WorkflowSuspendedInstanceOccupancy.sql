-- 109：把实例业务唯一占用从仅运行中扩展到暂停中，避免暂停后同业务再启动、恢复时撞唯一约束。
-- SQL Server 使用持久化计算列加过滤唯一索引；必须先删索引再按定义重建计算列，脚本可重复执行。
-- 不可逆风险：已存在“暂停实例 + 同业务新运行实例”脏数据时重建唯一索引会失败，必须先清理冲突行。
IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_instance')
      AND name = N'UX_fn_workflow_instance_ActiveBusinessKey'
)
    DROP INDEX UX_fn_workflow_instance_ActiveBusinessKey ON dbo.fn_workflow_instance;

IF EXISTS (
    SELECT 1
    FROM sys.computed_columns
    WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_instance')
      AND name = N'ActiveBusinessKey'
      AND definition NOT LIKE N'%suspended%'
)
    ALTER TABLE dbo.fn_workflow_instance DROP COLUMN ActiveBusinessKey;

IF COL_LENGTH(N'dbo.fn_workflow_instance', N'ActiveBusinessKey') IS NULL
BEGIN
    ALTER TABLE dbo.fn_workflow_instance ADD
        ActiveBusinessKey AS (
            CASE WHEN StatusKey IN ('active', 'suspended')
                THEN CONCAT(TenantScopeKey, N'|', BusinessType, N'|', BusinessId)
            END) PERSISTED;
END

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_instance')
      AND name = N'UX_fn_workflow_instance_ActiveBusinessKey'
)
    CREATE UNIQUE INDEX UX_fn_workflow_instance_ActiveBusinessKey
        ON dbo.fn_workflow_instance(ActiveBusinessKey)
        WHERE StatusKey IN ('active', 'suspended');

IF EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_workflow_instance')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_instance'), N'ActiveBusinessKey', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'占用中的实例业务唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'ActiveBusinessKey';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'占用中的实例业务唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_instance', @level2type=N'COLUMN', @level2name=N'ActiveBusinessKey';
