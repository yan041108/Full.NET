IF COL_LENGTH(N'dbo.fn_identity_user_profile', N'AvatarFileId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_user_profile
        ADD AvatarFileId uniqueidentifier NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'AvatarFileId', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'头像文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'AvatarFileId';
END;

IF COL_LENGTH(N'dbo.fn_identity_user_profile', N'SignatureFileId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_user_profile
        ADD SignatureFileId uniqueidentifier NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'SignatureFileId', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'签名文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'SignatureFileId';
END;

GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
      AND name = N'IX_fn_identity_user_profile_AvatarFileId')
BEGIN
    CREATE NONCLUSTERED INDEX IX_fn_identity_user_profile_AvatarFileId
        ON dbo.fn_identity_user_profile(AvatarFileId)
        WHERE AvatarFileId IS NOT NULL;
END;

GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
      AND name = N'IX_fn_identity_user_profile_SignatureFileId')
BEGIN
    CREATE NONCLUSTERED INDEX IX_fn_identity_user_profile_SignatureFileId
        ON dbo.fn_identity_user_profile(SignatureFileId)
        WHERE SignatureFileId IS NOT NULL;
END;
