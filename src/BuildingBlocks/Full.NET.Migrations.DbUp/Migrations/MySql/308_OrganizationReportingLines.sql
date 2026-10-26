-- 直属汇报关系独立于部门负责人；所有写入在当前租户自有门闩内完成。
CREATE TABLE IF NOT EXISTS fn_organization_reporting_guard (
    Id BINARY(16) NOT NULL COMMENT '应用端UUIDv7串行门闩标识',
    TenantId BINARY(16) NOT NULL COMMENT '可信租户标识；每个租户只有一个写门闩',
    CONSTRAINT pk_fn_organization_reporting_guard PRIMARY KEY (Id)
) COMMENT='租户直属汇报图写入串行门闩；不保存身份资料' ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS fn_organization_reporting_line (
    Id BINARY(16) NOT NULL COMMENT '应用端UUIDv7汇报关系标识',
    TenantId BINARY(16) NOT NULL COMMENT '认证上下文中的可信租户标识',
    EmployeeUserId BINARY(16) NOT NULL COMMENT '当前租户的人员标识；身份由Identity验证',
    ManagerUserId BINARY(16) NULL COMMENT '明确直属上级标识；空值表示没有直属上级',
    Revision BIGINT NOT NULL COMMENT '直属关系的乐观锁修订',
    UpdatedById BINARY(16) NOT NULL COMMENT '认证上下文中的实际操作人',
    UpdatedAtUtc DATETIME(6) NOT NULL COMMENT '最近变更时间；统一UTC微秒精度',
    CONSTRAINT pk_fn_organization_reporting_line PRIMARY KEY (Id)
) COMMENT='人员明确配置的直属汇报关系；不从部门或职级推定' ENGINE=InnoDB;
DROP PROCEDURE IF EXISTS fn_organization_restore_reporting_308;
DELIMITER $$
CREATE PROCEDURE fn_organization_restore_reporting_308()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_organization_reporting_guard' AND CONSTRAINT_NAME='ux_fn_organization_reporting_guard_tenant' AND CONSTRAINT_TYPE='UNIQUE') THEN
        ALTER TABLE fn_organization_reporting_guard ADD CONSTRAINT ux_fn_organization_reporting_guard_tenant UNIQUE (TenantId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_organization_reporting_line' AND CONSTRAINT_NAME='ux_fn_organization_reporting_line_employee' AND CONSTRAINT_TYPE='UNIQUE') THEN
        ALTER TABLE fn_organization_reporting_line ADD CONSTRAINT ux_fn_organization_reporting_line_employee UNIQUE (TenantId,EmployeeUserId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_organization_reporting_line' AND CONSTRAINT_NAME='fk_fn_organization_reporting_line_guard' AND CONSTRAINT_TYPE='FOREIGN KEY') THEN
        ALTER TABLE fn_organization_reporting_line ADD CONSTRAINT fk_fn_organization_reporting_line_guard FOREIGN KEY (TenantId) REFERENCES fn_organization_reporting_guard(TenantId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_organization_reporting_line' AND CONSTRAINT_NAME='ck_fn_organization_reporting_line_state' AND CONSTRAINT_TYPE='CHECK') THEN
        ALTER TABLE fn_organization_reporting_line ADD CONSTRAINT ck_fn_organization_reporting_line_state CHECK (Revision>0 AND EmployeeUserId<>0x00000000000000000000000000000000 AND (ManagerUserId IS NULL OR (ManagerUserId<>EmployeeUserId AND ManagerUserId<>0x00000000000000000000000000000000)));
    END IF;
END$$
DELIMITER ;
CALL fn_organization_restore_reporting_308();
DROP PROCEDURE IF EXISTS fn_organization_restore_reporting_308;
