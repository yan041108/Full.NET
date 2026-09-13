IF COL_LENGTH(N'dbo.fn_identity_user', N'MustChangePassword') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_user
        ADD MustChangePassword bit NOT NULL
            CONSTRAINT DF_fn_identity_user_MustChangePassword DEFAULT (0);

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_identity_user')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user'), N'MustChangePassword', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否必须改密', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'MustChangePassword';
END;

IF COL_LENGTH(N'dbo.fn_identity_user', N'PasswordChangedAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_user
        ADD PasswordChangedAtUtc datetimeoffset(7) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_identity_user')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user'), N'PasswordChangedAtUtc', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Password Changed At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'PasswordChangedAtUtc';
END;

GO

UPDATE dbo.fn_identity_user
SET PasswordChangedAtUtc = COALESCE(UpdatedAtUtc, CreatedAtUtc)
WHERE PasswordChangedAtUtc IS NULL;
