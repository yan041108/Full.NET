-- 开放注册与成员席位共享恢复阶段；来源方式为空的既有工作项保持原路径。
IF COL_LENGTH(N'dbo.fn_identity_member_seat_operation', N'RegistrationWayId') IS NULL
    ALTER TABLE dbo.fn_identity_member_seat_operation ADD RegistrationWayId uniqueidentifier NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND name=N'IX_fn_identity_member_seat_operation_User_Created')
    CREATE INDEX IX_fn_identity_member_seat_operation_User_Created ON dbo.fn_identity_member_seat_operation(UserId,CreatedAtUtc DESC,Id DESC);
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_identity_member_seat_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_member_seat_operation'),N'RegistrationWayId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'开放注册的来源方式；邀请与管理员开通为空',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_member_seat_operation',@level2type=N'COLUMN',@level2name=N'RegistrationWayId';
