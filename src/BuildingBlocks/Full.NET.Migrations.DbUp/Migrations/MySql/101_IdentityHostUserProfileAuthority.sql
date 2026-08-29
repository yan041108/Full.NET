-- 101：规范 Host 用户权威资料，并以数据库唯一索引关闭并发竞态。
DROP PROCEDURE IF EXISTS fn_identity_host_user_profile_authority;
DELIMITER $$
CREATE PROCEDURE fn_identity_host_user_profile_authority()
BEGIN
    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_identity_user_profile' AND INDEX_NAME = 'UX_fn_identity_user_profile_PhoneNumber') THEN
        ALTER TABLE fn_identity_user_profile DROP INDEX UX_fn_identity_user_profile_PhoneNumber;
    END IF;
    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_identity_user_profile' AND INDEX_NAME = 'UX_fn_identity_user_profile_Email') THEN
        ALTER TABLE fn_identity_user_profile DROP INDEX UX_fn_identity_user_profile_Email;
    END IF;
    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_identity_user_profile' AND INDEX_NAME = 'UX_fn_identity_user_profile_EmployeeNumber') THEN
        ALTER TABLE fn_identity_user_profile DROP INDEX UX_fn_identity_user_profile_EmployeeNumber;
    END IF;
    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_identity_user_profile' AND INDEX_NAME = 'UX_fn_identity_user_profile_IdCardType_IdCardNumber') THEN
        ALTER TABLE fn_identity_user_profile DROP INDEX UX_fn_identity_user_profile_IdCardType_IdCardNumber;
    END IF;

    -- 应用层已规范大小写；二进制排序规则让双提供程序的唯一性语义保持一致。
    ALTER TABLE fn_identity_user_profile
        MODIFY PhoneNumber varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL,
        MODIFY Email varchar(256) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL,
        MODIFY EmployeeNumber varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL,
        MODIFY IdCardType varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL,
        MODIFY IdCardNumber varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL;

    UPDATE fn_identity_user_profile
    SET PhoneNumber = NULLIF(TRIM(PhoneNumber), ''),
        Email = LOWER(NULLIF(TRIM(Email), '')),
        EmployeeNumber = UPPER(NULLIF(TRIM(EmployeeNumber), '')),
        IdCardType = LOWER(NULLIF(TRIM(IdCardType), '')),
        IdCardNumber = UPPER(NULLIF(TRIM(IdCardNumber), ''))
    WHERE (PhoneNumber IS NOT NULL AND (TRIM(PhoneNumber) = '' OR PhoneNumber <> TRIM(PhoneNumber)))
       OR (Email IS NOT NULL AND (TRIM(Email) = '' OR Email <> LOWER(TRIM(Email))))
       OR (EmployeeNumber IS NOT NULL AND (TRIM(EmployeeNumber) = '' OR EmployeeNumber <> UPPER(TRIM(EmployeeNumber))))
       OR (IdCardType IS NOT NULL AND (TRIM(IdCardType) = '' OR IdCardType <> LOWER(TRIM(IdCardType))))
       OR (IdCardNumber IS NOT NULL AND (TRIM(IdCardNumber) = '' OR IdCardNumber <> UPPER(TRIM(IdCardNumber))));

    IF EXISTS (SELECT PhoneNumber FROM fn_identity_user_profile WHERE PhoneNumber IS NOT NULL GROUP BY PhoneNumber HAVING COUNT(*) > 1) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Duplicate Host user phone numbers must be resolved before migration 101.';
    END IF;
    IF EXISTS (SELECT Email FROM fn_identity_user_profile WHERE Email IS NOT NULL GROUP BY Email HAVING COUNT(*) > 1) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Duplicate Host user emails must be resolved before migration 101.';
    END IF;
    IF EXISTS (SELECT EmployeeNumber FROM fn_identity_user_profile WHERE EmployeeNumber IS NOT NULL GROUP BY EmployeeNumber HAVING COUNT(*) > 1) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Duplicate Host user employee numbers must be resolved before migration 101.';
    END IF;
    IF EXISTS (SELECT IdCardType, IdCardNumber FROM fn_identity_user_profile WHERE IdCardType IS NOT NULL AND IdCardNumber IS NOT NULL GROUP BY IdCardType, IdCardNumber HAVING COUNT(*) > 1) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Duplicate Host user identity documents must be resolved before migration 101.';
    END IF;

    ALTER TABLE fn_identity_user_profile ADD UNIQUE INDEX UX_fn_identity_user_profile_PhoneNumber (PhoneNumber);

    ALTER TABLE fn_identity_user_profile ADD UNIQUE INDEX UX_fn_identity_user_profile_Email (Email);

    ALTER TABLE fn_identity_user_profile ADD UNIQUE INDEX UX_fn_identity_user_profile_EmployeeNumber (EmployeeNumber);

    ALTER TABLE fn_identity_user_profile ADD UNIQUE INDEX UX_fn_identity_user_profile_IdCardType_IdCardNumber (IdCardType, IdCardNumber);
END$$
DELIMITER ;

CALL fn_identity_host_user_profile_authority();
DROP PROCEDURE fn_identity_host_user_profile_authority;
