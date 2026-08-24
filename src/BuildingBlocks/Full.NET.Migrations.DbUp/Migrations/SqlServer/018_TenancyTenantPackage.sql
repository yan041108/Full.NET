-- 018：Host 租户套餐目录主数据；套餐与租户绑定在后续切片交付。
IF OBJECT_ID(N'dbo.fn_tenancy_tenant_package', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_tenancy_tenant_package
    (
        Id uniqueidentifier NOT NULL,
        Code varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Name nvarchar(128) NOT NULL,
        Description nvarchar(512) NULL,
        IsActive bit NOT NULL
            CONSTRAINT DF_fn_tenancy_tenant_package_IsActive DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_tenancy_tenant_package_Version DEFAULT (1),
        CONSTRAINT PK_fn_tenancy_tenant_package PRIMARY KEY CLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant_package')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户租户套餐表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant_package';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant_package')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant_package'), N'Code', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'编码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant_package', @level2type=N'COLUMN', @level2name=N'Code';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant_package')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant_package'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant_package', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant_package')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant_package'), N'Description', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'描述', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant_package', @level2type=N'COLUMN', @level2name=N'Description';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant_package')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant_package'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant_package', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant_package')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant_package'), N'IsActive', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant_package', @level2type=N'COLUMN', @level2name=N'IsActive';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant_package')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant_package'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant_package', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant_package')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant_package'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant_package', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant_package')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant_package'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant_package', @level2type=N'COLUMN', @level2name=N'Version';
    CREATE UNIQUE INDEX UX_fn_tenancy_tenant_package_Code
        ON dbo.fn_tenancy_tenant_package(Code);
END;
