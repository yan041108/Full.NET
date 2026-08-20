-- 098：任务定义增加 HandlerKind 与 ArgsJson，支持可配置 HTTP 执行器。

IF COL_LENGTH(N'dbo.fn_jobs_definition', N'HandlerKind') IS NULL
BEGIN
    ALTER TABLE dbo.fn_jobs_definition
        ADD HandlerKind varchar(32) NOT NULL
            CONSTRAINT DF_fn_jobs_definition_HandlerKind DEFAULT ('ping');
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内置执行器稳定机器码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_definition', @level2type=N'COLUMN', @level2name=N'HandlerKind';
END;

IF COL_LENGTH(N'dbo.fn_jobs_definition', N'ArgsJson') IS NULL
BEGIN
    ALTER TABLE dbo.fn_jobs_definition
        ADD ArgsJson nvarchar(max) NULL;
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'执行参数 JSON；ping 必须为 NULL', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_definition', @level2type=N'COLUMN', @level2name=N'ArgsJson';
END;
