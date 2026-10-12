-- 251：分类与标签目录事务门禁。只增加技术行，不清洗、重命名或删除历史文档。
IF OBJECT_ID(N'dbo.fn_document_taxonomy_write_guard', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_document_taxonomy_write_guard
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        CONSTRAINT PK_fn_document_taxonomy_write_guard PRIMARY KEY CLUSTERED (Id)
    );
END;

-- 结构完成而 DbUp 尚未记账时，约束、种子和描述都能独立恢复。
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_document_taxonomy_write_guard') AND name = N'CK_fn_document_taxonomy_write_guard_HostScope')
    ALTER TABLE dbo.fn_document_taxonomy_write_guard ADD CONSTRAINT CK_fn_document_taxonomy_write_guard_HostScope CHECK (TenantId IS NULL);

IF NOT EXISTS (SELECT 1 FROM dbo.fn_document_taxonomy_write_guard WHERE Id = '01a12ef8-d360-7d89-91f6-7e58ddbfcd73')
    INSERT INTO dbo.fn_document_taxonomy_write_guard (Id, TenantId) VALUES ('01a12ef8-d360-7d89-91f6-7e58ddbfcd73', NULL);

IF NOT EXISTS (SELECT 1 FROM dbo.fn_document_taxonomy_write_guard WHERE Id = '01a12ef8-d36c-78d9-b24b-93aa39be0747')
    INSERT INTO dbo.fn_document_taxonomy_write_guard (Id, TenantId) VALUES ('01a12ef8-d36c-78d9-b24b-93aa39be0747', NULL);

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_document_taxonomy_write_guard') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Host 分类与标签目录事务门禁', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_taxonomy_write_guard';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_document_taxonomy_write_guard') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_taxonomy_write_guard'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用生成的固定 UUID v7 技术行标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_taxonomy_write_guard', @level2type=N'COLUMN', @level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_document_taxonomy_write_guard') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_taxonomy_write_guard'), N'TenantId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'仅 Host 技术行，固定为 NULL', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_taxonomy_write_guard', @level2type=N'COLUMN', @level2name=N'TenantId';
