IF COL_LENGTH(N'dbo.fn_identity_user_profile', N'AvatarFileId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_user_profile
        ADD AvatarFileId uniqueidentifier NULL;
END;

IF COL_LENGTH(N'dbo.fn_identity_user_profile', N'SignatureFileId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_user_profile
        ADD SignatureFileId uniqueidentifier NULL;
END;

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
