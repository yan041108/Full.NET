-- 已接受的 TOTP 步长只递增；重跑不能重置二次认证重放防护。
IF COL_LENGTH(N'dbo.fn_identity_user_totp', N'LastAcceptedCounter') IS NULL
    ALTER TABLE dbo.fn_identity_user_totp ADD LastAcceptedCounter bigint NULL;
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1
    AND major_id = OBJECT_ID(N'dbo.fn_identity_user_totp')
    AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_totp'), N'LastAcceptedCounter', 'ColumnId')
    AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近接受的中心登录 TOTP 步长，阻止重放',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_totp',
        @level2type=N'COLUMN', @level2name=N'LastAcceptedCounter';
