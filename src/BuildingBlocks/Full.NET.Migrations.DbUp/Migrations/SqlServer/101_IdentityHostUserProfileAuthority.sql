-- 101：规范 Host 用户权威资料，并以数据库唯一索引关闭并发竞态。
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_identity_user_profile') AND name = N'UX_fn_identity_user_profile_PhoneNumber')
    DROP INDEX UX_fn_identity_user_profile_PhoneNumber ON dbo.fn_identity_user_profile;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_identity_user_profile') AND name = N'UX_fn_identity_user_profile_Email')
    DROP INDEX UX_fn_identity_user_profile_Email ON dbo.fn_identity_user_profile;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_identity_user_profile') AND name = N'UX_fn_identity_user_profile_EmployeeNumber')
    DROP INDEX UX_fn_identity_user_profile_EmployeeNumber ON dbo.fn_identity_user_profile;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_identity_user_profile') AND name = N'UX_fn_identity_user_profile_IdCardType_IdCardNumber')
    DROP INDEX UX_fn_identity_user_profile_IdCardType_IdCardNumber ON dbo.fn_identity_user_profile;

-- 应用层已规范大小写；二进制排序规则让双提供程序的唯一性语义保持一致。
ALTER TABLE dbo.fn_identity_user_profile ALTER COLUMN PhoneNumber nvarchar(32) COLLATE Latin1_General_100_BIN2 NULL;
ALTER TABLE dbo.fn_identity_user_profile ALTER COLUMN Email nvarchar(256) COLLATE Latin1_General_100_BIN2 NULL;
ALTER TABLE dbo.fn_identity_user_profile ALTER COLUMN EmployeeNumber nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL;
ALTER TABLE dbo.fn_identity_user_profile ALTER COLUMN IdCardType nvarchar(32) COLLATE Latin1_General_100_BIN2 NULL;
ALTER TABLE dbo.fn_identity_user_profile ALTER COLUMN IdCardNumber nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL;

UPDATE dbo.fn_identity_user_profile
SET PhoneNumber = NULLIF(LTRIM(RTRIM(PhoneNumber)), N''),
    Email = LOWER(NULLIF(LTRIM(RTRIM(Email)), N'')),
    EmployeeNumber = UPPER(NULLIF(LTRIM(RTRIM(EmployeeNumber)), N'')),
    IdCardType = LOWER(NULLIF(LTRIM(RTRIM(IdCardType)), N'')),
    IdCardNumber = UPPER(NULLIF(LTRIM(RTRIM(IdCardNumber)), N''))
WHERE (PhoneNumber IS NOT NULL AND (LTRIM(RTRIM(PhoneNumber)) = N'' OR PhoneNumber <> LTRIM(RTRIM(PhoneNumber))))
   OR (Email IS NOT NULL AND (LTRIM(RTRIM(Email)) = N'' OR Email <> LOWER(LTRIM(RTRIM(Email)))))
   OR (EmployeeNumber IS NOT NULL AND (LTRIM(RTRIM(EmployeeNumber)) = N'' OR EmployeeNumber <> UPPER(LTRIM(RTRIM(EmployeeNumber)))))
   OR (IdCardType IS NOT NULL AND (LTRIM(RTRIM(IdCardType)) = N'' OR IdCardType <> LOWER(LTRIM(RTRIM(IdCardType)))))
   OR (IdCardNumber IS NOT NULL AND (LTRIM(RTRIM(IdCardNumber)) = N'' OR IdCardNumber <> UPPER(LTRIM(RTRIM(IdCardNumber)))));

IF EXISTS (SELECT PhoneNumber FROM dbo.fn_identity_user_profile WHERE PhoneNumber IS NOT NULL GROUP BY PhoneNumber HAVING COUNT_BIG(*) > 1)
    THROW 51001, 'Duplicate Host user phone numbers must be resolved before migration 101.', 1;
IF EXISTS (SELECT Email FROM dbo.fn_identity_user_profile WHERE Email IS NOT NULL GROUP BY Email HAVING COUNT_BIG(*) > 1)
    THROW 51002, 'Duplicate Host user emails must be resolved before migration 101.', 1;
IF EXISTS (SELECT EmployeeNumber FROM dbo.fn_identity_user_profile WHERE EmployeeNumber IS NOT NULL GROUP BY EmployeeNumber HAVING COUNT_BIG(*) > 1)
    THROW 51003, 'Duplicate Host user employee numbers must be resolved before migration 101.', 1;
IF EXISTS (SELECT IdCardType, IdCardNumber FROM dbo.fn_identity_user_profile WHERE IdCardType IS NOT NULL AND IdCardNumber IS NOT NULL GROUP BY IdCardType, IdCardNumber HAVING COUNT_BIG(*) > 1)
    THROW 51004, 'Duplicate Host user identity documents must be resolved before migration 101.', 1;

CREATE UNIQUE INDEX UX_fn_identity_user_profile_PhoneNumber ON dbo.fn_identity_user_profile(PhoneNumber) WHERE PhoneNumber IS NOT NULL;

CREATE UNIQUE INDEX UX_fn_identity_user_profile_Email ON dbo.fn_identity_user_profile(Email) WHERE Email IS NOT NULL;

CREATE UNIQUE INDEX UX_fn_identity_user_profile_EmployeeNumber ON dbo.fn_identity_user_profile(EmployeeNumber) WHERE EmployeeNumber IS NOT NULL;

CREATE UNIQUE INDEX UX_fn_identity_user_profile_IdCardType_IdCardNumber ON dbo.fn_identity_user_profile(IdCardType, IdCardNumber) WHERE IdCardType IS NOT NULL AND IdCardNumber IS NOT NULL;
