-- 每一步只执行固定 DDL，部分提交后重入补齐列和索引，不接受外部标识符。
DROP PROCEDURE IF EXISTS fn_identity_registration_enrollment_recovery;
DELIMITER $$
CREATE PROCEDURE fn_identity_registration_enrollment_recovery()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_identity_member_seat_operation' AND COLUMN_NAME='RegistrationWayId') THEN
        ALTER TABLE fn_identity_member_seat_operation ADD COLUMN RegistrationWayId BINARY(16) NULL COMMENT '开放注册的来源方式；邀请与管理员开通为空';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_identity_member_seat_operation' AND INDEX_NAME='IX_fn_identity_member_seat_operation_User_Created') THEN
        CREATE INDEX IX_fn_identity_member_seat_operation_User_Created ON fn_identity_member_seat_operation(UserId,CreatedAtUtc DESC,Id DESC);
    END IF;
END$$
DELIMITER ;
CALL fn_identity_registration_enrollment_recovery();
DROP PROCEDURE IF EXISTS fn_identity_registration_enrollment_recovery;
