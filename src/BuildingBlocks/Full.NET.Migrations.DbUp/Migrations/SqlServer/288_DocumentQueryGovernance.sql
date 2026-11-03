-- 分类树事务闸门与逻辑访问去重。固定闸门标识由应用 UUID v7 生成器预生成；两库使用相同网络字节序。
-- DDL、种子和索引分别探测，可从未记账的半完成状态恢复；不删除既有访问事实。
IF OBJECT_ID('dbo.fn_document_category_guard', 'U') IS NULL
    CREATE TABLE dbo.fn_document_category_guard (
        Id uniqueidentifier NOT NULL,
        Version bigint NOT NULL,
        CONSTRAINT PK_fn_document_category_guard PRIMARY KEY NONCLUSTERED (Id)
    );
GO
IF NOT EXISTS (SELECT 1 FROM dbo.fn_document_category_guard WHERE Id = '019a1900-0000-7000-8000-000000000001')
    INSERT INTO dbo.fn_document_category_guard (Id, Version) VALUES ('019a1900-0000-7000-8000-000000000001', 1);
GO
IF COL_LENGTH('dbo.fn_document_access_log', 'OperationKey') IS NULL
    ALTER TABLE dbo.fn_document_access_log ADD OperationKey varchar(192) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.fn_document_access_log') AND name = 'UX_fn_document_access_log_OperationKey')
    CREATE UNIQUE INDEX UX_fn_document_access_log_OperationKey ON dbo.fn_document_access_log (OperationKey) WHERE OperationKey IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_category_guard') AND minor_id = 0 AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档分类树事务闸门表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_category_guard';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_category_guard') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_category_guard'), 'Id', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预生成的 UUID v7 Host 闸门标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_category_guard', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_category_guard') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_category_guard'), 'Version', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'取得锁时递增的闸门版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_category_guard', @level2type=N'COLUMN', @level2name=N'Version';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_access_log') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_access_log'), 'OperationKey', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑访问身份，重试与 Range 请求归并', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_access_log', @level2type=N'COLUMN', @level2name=N'OperationKey';
GO
