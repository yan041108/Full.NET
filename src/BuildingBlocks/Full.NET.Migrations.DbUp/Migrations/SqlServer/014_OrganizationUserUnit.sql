IF OBJECT_ID(N'dbo.fn_organization_user_unit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_organization_user_unit
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        UnitId uniqueidentifier NOT NULL,
        IsPrimary bit NOT NULL,
        IsActive bit NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL CONSTRAINT DF_fn_organization_user_unit_Version DEFAULT (1),
        CONSTRAINT PK_fn_organization_user_unit PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_organization_user_unit_Unit
            FOREIGN KEY (UnitId) REFERENCES dbo.fn_organization_unit(Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_user_unit')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'组织机构用户机构表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_user_unit';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_user_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_user_unit'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_user_unit', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_user_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_user_unit'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_user_unit', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_user_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_user_unit'), N'IsActive', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_user_unit', @level2type=N'COLUMN', @level2name=N'IsActive';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_user_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_user_unit'), N'IsPrimary', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否主关联', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_user_unit', @level2type=N'COLUMN', @level2name=N'IsPrimary';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_user_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_user_unit'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_user_unit', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_user_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_user_unit'), N'UnitId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'机构单元标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_user_unit', @level2type=N'COLUMN', @level2name=N'UnitId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_user_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_user_unit'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_user_unit', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_user_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_user_unit'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_user_unit', @level2type=N'COLUMN', @level2name=N'UserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_organization_user_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_user_unit'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_user_unit', @level2type=N'COLUMN', @level2name=N'Version';
    CREATE UNIQUE INDEX UX_fn_organization_user_unit_Tenant_User_Unit
        ON dbo.fn_organization_user_unit(TenantId, UserId, UnitId);
    CREATE INDEX IX_fn_organization_user_unit_Tenant_User
        ON dbo.fn_organization_user_unit(TenantId, UserId, IsPrimary)
        WHERE IsActive = 1;
    CREATE INDEX IX_fn_organization_user_unit_Tenant_Unit
        ON dbo.fn_organization_user_unit(TenantId, UnitId)
        WHERE IsActive = 1;
    CREATE CLUSTERED INDEX CX_fn_organization_user_unit_Tenant_Created
        ON dbo.fn_organization_user_unit(TenantId, CreatedAtUtc);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_organization_user_unit')
      AND name = N'DF_fn_organization_user_unit_IsPrimary'
)
    ALTER TABLE dbo.fn_organization_user_unit
        ADD CONSTRAINT DF_fn_organization_user_unit_IsPrimary DEFAULT (0) FOR IsPrimary;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_organization_user_unit')
      AND name = N'DF_fn_organization_user_unit_IsActive'
)
    ALTER TABLE dbo.fn_organization_user_unit
        ADD CONSTRAINT DF_fn_organization_user_unit_IsActive DEFAULT (1) FOR IsActive;
