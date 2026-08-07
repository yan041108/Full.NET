-- 083 移除 Identity 数据范围表指向 Organization 的跨模块外键；引用完整性由应用层校验承担。
IF EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_fn_identity_role_data_scope_unit_Unit'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_identity_role_data_scope_unit'))
BEGIN
    ALTER TABLE dbo.fn_identity_role_data_scope_unit
        DROP CONSTRAINT FK_fn_identity_role_data_scope_unit_Unit;
END;