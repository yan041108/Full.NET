-- 096：为作业定义增加是否允许重叠执行，对标 Admin.NET SysJobDetail.Concurrent。
-- 默认 0（禁止重叠），与 Full.NET 更安全默认值一致；Admin.NET 迁移时 AllowConcurrentExecutions = Concurrent。

IF COL_LENGTH(N'dbo.fn_jobs_definition', N'AllowConcurrentExecutions') IS NULL
BEGIN
    ALTER TABLE dbo.fn_jobs_definition
        ADD AllowConcurrentExecutions bit NOT NULL
            CONSTRAINT DF_fn_jobs_definition_AllowConcurrentExecutions DEFAULT (0);
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_jobs_definition')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_definition'), N'AllowConcurrentExecutions', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否允许同一作业重叠执行', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_definition', @level2type=N'COLUMN', @level2name=N'AllowConcurrentExecutions';
END;
