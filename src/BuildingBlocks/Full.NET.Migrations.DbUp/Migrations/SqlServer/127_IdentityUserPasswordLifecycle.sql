IF COL_LENGTH(N'dbo.fn_identity_user', N'MustChangePassword') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_user
        ADD MustChangePassword bit NOT NULL
            CONSTRAINT DF_fn_identity_user_MustChangePassword DEFAULT (0);
END;

IF COL_LENGTH(N'dbo.fn_identity_user', N'PasswordChangedAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_user
        ADD PasswordChangedAtUtc datetimeoffset(7) NULL;
END;

UPDATE dbo.fn_identity_user
SET PasswordChangedAtUtc = COALESCE(UpdatedAtUtc, CreatedAtUtc)
WHERE PasswordChangedAtUtc IS NULL;
