-- 041：角色字段授权只保存稳定语义键，作用域和租户边界始终从角色表验证。
IF OBJECT_ID(N'dbo.fn_identity_role_field_grant', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_role_field_grant
    (
        Id uniqueidentifier NOT NULL,
        RoleId uniqueidentifier NOT NULL,
        ResourceKey varchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
        FieldKey varchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CreatedById uniqueidentifier NOT NULL,
        CONSTRAINT PK_fn_identity_role_field_grant PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_identity_role_field_grant_Role
            FOREIGN KEY (RoleId) REFERENCES dbo.fn_identity_role(Id),
        CONSTRAINT CK_fn_identity_role_field_grant_ResourceKey
            CHECK (LEN(ResourceKey) BETWEEN 3 AND 160),
        CONSTRAINT CK_fn_identity_role_field_grant_FieldKey
            CHECK (LEN(FieldKey) BETWEEN 1 AND 160)
    );
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证角色字段授权表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role_field_grant';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role_field_grant', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role_field_grant', @level2type=N'COLUMN', @level2name=N'CreatedById';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'字段键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role_field_grant', @level2type=N'COLUMN', @level2name=N'FieldKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role_field_grant', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'资源键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role_field_grant', @level2type=N'COLUMN', @level2name=N'ResourceKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'角色标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role_field_grant', @level2type=N'COLUMN', @level2name=N'RoleId';
END;

IF EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_identity_role_field_grant')
      AND indexObject.name = N'UX_fn_identity_role_field_grant_RoleResourceField'
      AND
      (
          indexObject.is_unique = 0
          OR indexObject.has_filter = 1
          OR indexObject.is_disabled = 1
          OR
          (
              SELECT COUNT(*)
              FROM sys.index_columns AS indexColumn
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal > 0
          ) <> 3
          OR NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS indexColumn
              INNER JOIN sys.columns AS columnObject
                  ON columnObject.object_id = indexColumn.object_id
                 AND columnObject.column_id = indexColumn.column_id
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal = 1
                AND columnObject.name = N'RoleId'
          )
          OR NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS indexColumn
              INNER JOIN sys.columns AS columnObject
                  ON columnObject.object_id = indexColumn.object_id
                 AND columnObject.column_id = indexColumn.column_id
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal = 2
                AND columnObject.name = N'ResourceKey'
          )
          OR NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS indexColumn
              INNER JOIN sys.columns AS columnObject
                  ON columnObject.object_id = indexColumn.object_id
                 AND columnObject.column_id = indexColumn.column_id
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal = 3
                AND columnObject.name = N'FieldKey'
          )
      )
)
BEGIN
    DROP INDEX UX_fn_identity_role_field_grant_RoleResourceField
        ON dbo.fn_identity_role_field_grant;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_identity_role_field_grant')
      AND name = N'UX_fn_identity_role_field_grant_RoleResourceField'
)
BEGIN
    CREATE UNIQUE INDEX UX_fn_identity_role_field_grant_RoleResourceField
        ON dbo.fn_identity_role_field_grant(RoleId, ResourceKey, FieldKey);
END;
