IF OBJECT_ID(N'dbo.fn_organization_unit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_organization_unit
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        ParentId uniqueidentifier NULL,
        Code varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Name nvarchar(128) NOT NULL,
        DisplayOrder int NOT NULL,
        IsActive bit NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL CONSTRAINT DF_fn_organization_unit_Version DEFAULT (1),
        CONSTRAINT PK_fn_organization_unit PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_organization_unit_Parent
            FOREIGN KEY (ParentId) REFERENCES dbo.fn_organization_unit(Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_unit')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'组织机构机构单元表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_unit';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_unit'), N'Code', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'编码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_unit', @level2type=N'COLUMN', @level2name=N'Code';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_unit'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_unit', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_unit'), N'DisplayOrder', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示顺序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_unit', @level2type=N'COLUMN', @level2name=N'DisplayOrder';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_unit'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_unit', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_unit'), N'IsActive', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_unit', @level2type=N'COLUMN', @level2name=N'IsActive';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_unit'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_unit', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_unit'), N'ParentId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'父级标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_unit', @level2type=N'COLUMN', @level2name=N'ParentId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_unit'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_unit', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_unit'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_unit', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_unit'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_unit', @level2type=N'COLUMN', @level2name=N'Version';
    CREATE UNIQUE INDEX UX_fn_organization_unit_Tenant_Code
        ON dbo.fn_organization_unit(TenantId, Code);
    CREATE INDEX IX_fn_organization_unit_Tenant_Parent
        ON dbo.fn_organization_unit(TenantId, ParentId, DisplayOrder)
        WHERE IsActive = 1;
    CREATE CLUSTERED INDEX CX_fn_organization_unit_Tenant_Created
        ON dbo.fn_organization_unit(TenantId, CreatedAtUtc);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_organization_unit')
      AND name = N'DF_fn_organization_unit_IsActive'
)
    ALTER TABLE dbo.fn_organization_unit
        ADD CONSTRAINT DF_fn_organization_unit_IsActive DEFAULT (1) FOR IsActive;
