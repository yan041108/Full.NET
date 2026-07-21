SET @column_exists := (
    SELECT COUNT(1)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_identity_role'
      AND COLUMN_NAME = 'DataScopeKind');
SET @ddl := IF(
    @column_exists = 0,
    'ALTER TABLE fn_identity_role ADD COLUMN DataScopeKind varchar(64) NOT NULL DEFAULT ''identity.data_scope.all''',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

CREATE TABLE IF NOT EXISTS fn_identity_role_data_scope_unit
(
    RoleId char(36) NOT NULL,
    UnitId char(36) NOT NULL,
    CONSTRAINT PK_fn_identity_role_data_scope_unit PRIMARY KEY (RoleId, UnitId),
    CONSTRAINT FK_fn_identity_role_data_scope_unit_Role
        FOREIGN KEY (RoleId) REFERENCES fn_identity_role(Id),
    CONSTRAINT FK_fn_identity_role_data_scope_unit_Unit
        FOREIGN KEY (UnitId) REFERENCES fn_organization_unit(Id)
);

UPDATE fn_identity_role
SET DataScopeKind = 'identity.data_scope.all'
WHERE DataScopeKind IS NULL OR TRIM(DataScopeKind) = '';
