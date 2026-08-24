-- 035：租户职级目录（非聚集主键 + 租户创建时间聚集索引）。
IF OBJECT_ID(N'dbo.fn_organization_position_level', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_organization_position_level
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        Code varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Name nvarchar(128) NOT NULL,
        DisplayOrder int NOT NULL,
        IsActive bit NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL CONSTRAINT DF_fn_organization_position_level_Version DEFAULT (1),
        CONSTRAINT PK_fn_organization_position_level PRIMARY KEY NONCLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_position_level')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'组织机构职级表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_position_level';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_position_level')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_position_level'), N'Code', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'编码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_position_level', @level2type=N'COLUMN', @level2name=N'Code';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_position_level')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_position_level'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_position_level', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_position_level')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_position_level'), N'DisplayOrder', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示顺序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_position_level', @level2type=N'COLUMN', @level2name=N'DisplayOrder';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_position_level')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_position_level'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_position_level', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_position_level')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_position_level'), N'IsActive', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_position_level', @level2type=N'COLUMN', @level2name=N'IsActive';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_position_level')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_position_level'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_position_level', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_position_level')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_position_level'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_position_level', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_position_level')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_position_level'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_position_level', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_position_level')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_position_level'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_position_level', @level2type=N'COLUMN', @level2name=N'Version';
    CREATE UNIQUE INDEX UX_fn_organization_position_level_Tenant_Code
        ON dbo.fn_organization_position_level(TenantId, Code);
    CREATE INDEX IX_fn_organization_position_level_Tenant_DisplayOrder
        ON dbo.fn_organization_position_level(TenantId, DisplayOrder, Code)
        WHERE IsActive = 1;
    CREATE CLUSTERED INDEX CX_fn_organization_position_level_Tenant_Created
        ON dbo.fn_organization_position_level(TenantId, CreatedAtUtc);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_organization_position_level')
      AND name = N'DF_fn_organization_position_level_IsActive'
)
    ALTER TABLE dbo.fn_organization_position_level
        ADD CONSTRAINT DF_fn_organization_position_level_IsActive DEFAULT (1) FOR IsActive;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_organization_position_level')
      AND name = N'UX_fn_organization_position_level_Tenant_Code'
)
    CREATE UNIQUE INDEX UX_fn_organization_position_level_Tenant_Code
        ON dbo.fn_organization_position_level(TenantId, Code);

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_organization_position_level')
      AND name = N'IX_fn_organization_position_level_Tenant_DisplayOrder'
)
    CREATE INDEX IX_fn_organization_position_level_Tenant_DisplayOrder
        ON dbo.fn_organization_position_level(TenantId, DisplayOrder, Code)
        WHERE IsActive = 1;
