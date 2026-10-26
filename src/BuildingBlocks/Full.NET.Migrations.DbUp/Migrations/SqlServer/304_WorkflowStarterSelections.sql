-- 发起人选择在启动事务中固定；后续激活只消费不可变输入并复核当前资格。
IF OBJECT_ID(N'dbo.fn_workflow_starter_selection',N'U') IS NULL
BEGIN
CREATE TABLE dbo.fn_workflow_starter_selection (
    Id uniqueidentifier NOT NULL,
    SelectionsJson nvarchar(max) NOT NULL,
    SelectionsHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CreatedAtUtc datetimeoffset(7) NOT NULL,
    CONSTRAINT pk_fn_workflow_starter_selection PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT fk_fn_workflow_starter_selection_instance FOREIGN KEY (Id) REFERENCES dbo.fn_workflow_instance(Id)
);
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_starter_selection') AND name=N'ck_fn_workflow_starter_selection_hash')
    ALTER TABLE dbo.fn_workflow_starter_selection ADD CONSTRAINT ck_fn_workflow_starter_selection_hash CHECK (LEN(SelectionsHash)=64);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_starter_selection') AND name=N'ck_fn_workflow_starter_selection_json')
    ALTER TABLE dbo.fn_workflow_starter_selection ADD CONSTRAINT ck_fn_workflow_starter_selection_json
        CHECK (ISJSON(SelectionsJson)=1 AND LEFT(LTRIM(SelectionsJson),1)=N'{' AND DATALENGTH(SelectionsJson)<=65536);
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_starter_selection') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实例启动时固定的发起人自选快照；不授予永久办理资格', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_starter_selection';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_starter_selection') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_starter_selection'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实例标识，同时作为一对一选择快照主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_starter_selection', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_starter_selection') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_starter_selection'),N'SelectionsJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'有界选择键与保序成员数组；不包含租户或发起人声明', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_starter_selection', @level2type=N'COLUMN', @level2name=N'SelectionsJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_starter_selection') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_starter_selection'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'随实例首次启动原子保存的UTC时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_starter_selection', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_starter_selection') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_starter_selection'),N'SelectionsHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'规范化选择快照的SHA256摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_starter_selection', @level2type=N'COLUMN', @level2name=N'SelectionsHash';
GO
