IF COL_LENGTH(N'dbo.fn_identity_role', N'DataScopeKind') IS NULL
    ALTER TABLE dbo.fn_identity_role
        ADD DataScopeKind varchar(64) NOT NULL
            CONSTRAINT DF_fn_identity_role_DataScopeKind
            DEFAULT ('identity.data_scope.all');
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_identity_role')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_role'), N'DataScopeKind', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'数据范围类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role', @level2type=N'COLUMN', @level2name=N'DataScopeKind';

IF OBJECT_ID(N'dbo.fn_identity_role_data_scope_unit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_role_data_scope_unit
    (
        RoleId uniqueidentifier NOT NULL,
        UnitId uniqueidentifier NOT NULL,
        CONSTRAINT PK_fn_identity_role_data_scope_unit PRIMARY KEY NONCLUSTERED (RoleId, UnitId),
        CONSTRAINT FK_fn_identity_role_data_scope_unit_Role
            FOREIGN KEY (RoleId) REFERENCES dbo.fn_identity_role(Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_role_data_scope_unit')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证角色数据范围机构表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role_data_scope_unit';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_role_data_scope_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_role_data_scope_unit'), N'RoleId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'角色标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role_data_scope_unit', @level2type=N'COLUMN', @level2name=N'RoleId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_role_data_scope_unit')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_role_data_scope_unit'), N'UnitId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'机构单元标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role_data_scope_unit', @level2type=N'COLUMN', @level2name=N'UnitId';
    CREATE CLUSTERED INDEX CX_fn_identity_role_data_scope_unit_Role
        ON dbo.fn_identity_role_data_scope_unit(RoleId);
END;

EXEC(N'
UPDATE dbo.fn_identity_role
SET DataScopeKind = ''identity.data_scope.all''
WHERE DataScopeKind IS NULL OR LTRIM(RTRIM(DataScopeKind)) = '''';');