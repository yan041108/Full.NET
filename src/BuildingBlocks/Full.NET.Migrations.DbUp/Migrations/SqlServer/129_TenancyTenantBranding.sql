IF COL_LENGTH(N'dbo.fn_tenancy_tenant', N'LogoFileId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_tenancy_tenant
        ADD LogoFileId uniqueidentifier NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'LogoFileId', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'标志文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'LogoFileId';
END;

IF COL_LENGTH(N'dbo.fn_tenancy_tenant', N'SystemTitle') IS NULL
BEGIN
    ALTER TABLE dbo.fn_tenancy_tenant
        ADD SystemTitle nvarchar(128) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'SystemTitle', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'系统标题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'SystemTitle';
END;

IF COL_LENGTH(N'dbo.fn_tenancy_tenant', N'ContactPhone') IS NULL
BEGIN
    ALTER TABLE dbo.fn_tenancy_tenant
        ADD ContactPhone nvarchar(32) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'ContactPhone', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'联系电话', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'ContactPhone';
END;

IF COL_LENGTH(N'dbo.fn_tenancy_tenant', N'ContactEmail') IS NULL
BEGIN
    ALTER TABLE dbo.fn_tenancy_tenant
        ADD ContactEmail nvarchar(256) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'ContactEmail', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'联系邮箱', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'ContactEmail';
END;

IF COL_LENGTH(N'dbo.fn_tenancy_tenant', N'ContactAddress') IS NULL
BEGIN
    ALTER TABLE dbo.fn_tenancy_tenant
        ADD ContactAddress nvarchar(512) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'ContactAddress', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'联系地址', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'ContactAddress';
END;

IF COL_LENGTH(N'dbo.fn_tenancy_tenant', N'Copyright') IS NULL
BEGIN
    ALTER TABLE dbo.fn_tenancy_tenant
        ADD Copyright nvarchar(256) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'Copyright', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'版权声明', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'Copyright';
END;

GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')
      AND name = N'IX_fn_tenancy_tenant_LogoFileId')
BEGIN
    CREATE NONCLUSTERED INDEX IX_fn_tenancy_tenant_LogoFileId
        ON dbo.fn_tenancy_tenant(LogoFileId)
        WHERE LogoFileId IS NOT NULL;
END;
