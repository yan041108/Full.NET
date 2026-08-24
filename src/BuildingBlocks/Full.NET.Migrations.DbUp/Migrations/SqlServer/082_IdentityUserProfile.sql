IF OBJECT_ID(N'dbo.fn_identity_user_profile', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_user_profile
    (
        UserId uniqueidentifier NOT NULL,
        Nickname nvarchar(128) NULL,
        PhoneNumber nvarchar(32) NULL,
        Email nvarchar(256) NULL,
        EmployeeNumber nvarchar(64) NULL,
        Gender varchar(16) NULL,
        JoinDateUtc date NULL,
        SortOrder int NOT NULL CONSTRAINT DF_fn_identity_user_profile_SortOrder DEFAULT (100),
        IdCardType nvarchar(32) NULL,
        IdCardNumber nvarchar(64) NULL,
        BirthDate date NULL,
        Ethnicity nvarchar(64) NULL,
        Address nvarchar(512) NULL,
        GraduatedSchool nvarchar(256) NULL,
        EducationLevel nvarchar(64) NULL,
        PoliticalStatus nvarchar(64) NULL,
        OfficePhone nvarchar(32) NULL,
        EmergencyContact nvarchar(128) NULL,
        EmergencyContactPhone nvarchar(32) NULL,
        EmergencyContactAddress nvarchar(512) NULL,
        Remark nvarchar(512) NULL,
        Version int NOT NULL CONSTRAINT DF_fn_identity_user_profile_Version DEFAULT (1),
        CONSTRAINT PK_fn_identity_user_profile PRIMARY KEY CLUSTERED (UserId),
        CONSTRAINT FK_fn_identity_user_profile_User
            FOREIGN KEY (UserId) REFERENCES dbo.fn_identity_user(Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证用户资料表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'Address', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'地址', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'Address';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'BirthDate', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'出生日期', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'BirthDate';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'EducationLevel', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'学历', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'EducationLevel';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'Email', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Email', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'Email';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'EmergencyContact', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'紧急联系人', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'EmergencyContact';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'EmergencyContactAddress', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'紧急联系人地址', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'EmergencyContactAddress';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'EmergencyContactPhone', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'紧急联系人电话', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'EmergencyContactPhone';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'EmployeeNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'EmployeeNumber';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'Ethnicity', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'民族', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'Ethnicity';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'Gender', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'性别', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'Gender';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'GraduatedSchool', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'毕业院校', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'GraduatedSchool';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'IdCardNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'证件号码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'IdCardNumber';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'IdCardType', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'证件类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'IdCardType';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'JoinDateUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'入职时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'JoinDateUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'Nickname', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'昵称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'Nickname';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'OfficePhone', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'办公电话', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'OfficePhone';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'PhoneNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'手机号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'PhoneNumber';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'PoliticalStatus', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'政治面貌', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'PoliticalStatus';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'Remark', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'备注', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'Remark';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'SortOrder', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'排序顺序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'SortOrder';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'UserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user_profile')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user_profile'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user_profile', @level2type=N'COLUMN', @level2name=N'Version';
END;