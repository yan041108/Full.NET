-- 负责人只有显式权威配置，不回填推定的普通成员。
CREATE TABLE IF NOT EXISTS fn_organization_unit_leadership (
    Id BINARY(16) NOT NULL COMMENT '复用机构逻辑主键的一对一配置标识',
    TenantId BINARY(16) NOT NULL COMMENT '由当前可信租户及所属机构确定的租户标识',
    LeaderUserId BINARY(16) NULL COMMENT '明确配置的负责人；空值表示尚无负责人',
    Revision BIGINT NOT NULL COMMENT '负责人配置的乐观锁修订',
    UpdatedById BINARY(16) NOT NULL COMMENT '认证上下文中的实际配置人标识',
    UpdatedAtUtc DATETIME(6) NOT NULL COMMENT '最近配置时间；统一UTC微秒精度',
    CONSTRAINT pk_fn_organization_unit_leadership PRIMARY KEY (Id)
) COMMENT='机构明确授权的负责人配置；不从隶属成员推定';
DROP PROCEDURE IF EXISTS fn_organization_restore_leadership_307;
DELIMITER $$
CREATE PROCEDURE fn_organization_restore_leadership_307()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_organization_unit_leadership' AND CONSTRAINT_NAME='fk_fn_organization_unit_leadership_unit' AND CONSTRAINT_TYPE='FOREIGN KEY') THEN
        ALTER TABLE fn_organization_unit_leadership ADD CONSTRAINT fk_fn_organization_unit_leadership_unit FOREIGN KEY (Id) REFERENCES fn_organization_unit(Id) ON DELETE CASCADE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_organization_unit_leadership' AND CONSTRAINT_NAME='ck_fn_organization_unit_leadership_state' AND CONSTRAINT_TYPE='CHECK') THEN
        ALTER TABLE fn_organization_unit_leadership ADD CONSTRAINT ck_fn_organization_unit_leadership_state
            CHECK (Revision>0 AND (LeaderUserId IS NULL OR LeaderUserId<>0x00000000000000000000000000000000));
    END IF;
END$$
DELIMITER ;
CALL fn_organization_restore_leadership_307();
DROP PROCEDURE IF EXISTS fn_organization_restore_leadership_307;
