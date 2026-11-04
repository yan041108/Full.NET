-- Identity 所有者原子交接的不可变回执；保留历史账号和成员标识，不建立删除级联。
-- MySQL DDL 非事务；整表创建原子，索引单独探测恢复遗漏，回退须保留已提交回执。
-- 账号 Owner 保护按 UserId/MemberRole 做有界存在性查询，不枚举该账号跨企业的成员清单。
CREATE TABLE IF NOT EXISTS fn_identity_tenant_owner_transfer (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    TenantId binary(16) NOT NULL COMMENT '所属企业标识',
    OperationId binary(16) NOT NULL COMMENT '调用方固定的交接操作标识',
    ActorUserId binary(16) NOT NULL COMMENT '实际交接操作者账号标识',
    SourceMemberId binary(16) NULL COMMENT '交接前所有者成员标识，首次建立为空',
    SourceUserId binary(16) NULL COMMENT '交接前所有者账号标识，首次建立为空',
    SourcePreviousVersion int NULL COMMENT '交接前源成员版本',
    SourceVersion int NULL COMMENT '交接后源成员版本',
    TargetMemberId binary(16) NOT NULL COMMENT '接收职责的成员标识',
    TargetUserId binary(16) NOT NULL COMMENT '接收职责的账号标识',
    TargetPreviousVersion int NOT NULL COMMENT '交接前目标成员版本',
    TargetVersion int NOT NULL COMMENT '交接后目标成员版本',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '交接事务提交事实时间(UTC)',
    RequestDigest char(64) NOT NULL COMMENT '仅绑定非敏感目标与版本的请求摘要',
    CONSTRAINT PK_fn_identity_tenant_owner_transfer PRIMARY KEY (Id)
) COMMENT='企业所有者交接不可变回执及领域审计，不保存密码或TOTP' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
DROP PROCEDURE IF EXISTS fn_identity_owner_transfer_indexes;
DELIMITER $$
CREATE PROCEDURE fn_identity_owner_transfer_indexes()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_identity_tenant_member' AND INDEX_NAME='IX_fn_identity_tenant_member_TenantId_MemberRole_Id') THEN
        CREATE INDEX IX_fn_identity_tenant_member_TenantId_MemberRole_Id ON fn_identity_tenant_member(TenantId,MemberRole,Id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_identity_tenant_member' AND INDEX_NAME='IX_fn_identity_tenant_member_UserId_MemberRole') THEN
        CREATE INDEX IX_fn_identity_tenant_member_UserId_MemberRole ON fn_identity_tenant_member(UserId,MemberRole);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_identity_tenant_owner_transfer' AND INDEX_NAME='UX_fn_identity_tenant_owner_transfer_TenantOperation') THEN
        CREATE UNIQUE INDEX UX_fn_identity_tenant_owner_transfer_TenantOperation ON fn_identity_tenant_owner_transfer(TenantId,OperationId);
    END IF;
END$$
DELIMITER ;
CALL fn_identity_owner_transfer_indexes();
DROP PROCEDURE IF EXISTS fn_identity_owner_transfer_indexes;
