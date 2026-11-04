-- MySQL DDL分阶段恢复；只增加准备指针，不回写活动密钥或现有业务事实。
CREATE TABLE IF NOT EXISTS fn_webhooks_key_rotation (
    Id binary(16) NOT NULL COMMENT '应用端UUID v7主键及新公开密钥身份',
    TenantId binary(16) NOT NULL COMMENT '可信企业标识',
    OperationId binary(16) NOT NULL COMMENT '固定准备操作，未知结果不得替换',
    ActorUserId binary(16) NOT NULL COMMENT '准备操作者',
    ActorSessionId binary(16) NOT NULL COMMENT '准备提交时交互会话',
    SubscriptionId binary(16) NOT NULL COMMENT '原订阅标识',
    PreviousKeyId binary(16) NOT NULL COMMENT '准备时原活动密钥身份',
    PreviousVersion int NOT NULL COMMENT '准备前订阅修订',
    PreparedVersion int NOT NULL COMMENT '准备提交后订阅修订',
    StateKey varchar(16) NOT NULL COMMENT 'prepared或activated或cancelled',
    ProtectedSecret varchar(1024) NULL COMMENT '准备期间的加密秘密；激活或取消后清除',
    ProtectedFingerprint varchar(1024) NOT NULL COMMENT '隔离用途加密的原输入摘要，仅用于幂等核对',
    PreparedAtUtc datetime(6) NOT NULL COMMENT '准备提交UTC时间',
    CompletedAtUtc datetime(6) NULL COMMENT '激活或取消提交UTC时间',
    CompletedVersion int NULL COMMENT '完成提交后的订阅修订',
    CompletedByUserId binary(16) NULL COMMENT '实际完成操作者',
    CompletedBySessionId binary(16) NULL COMMENT '完成提交时交互会话',
    CONSTRAINT PK_fn_webhooks_key_rotation PRIMARY KEY (Id),
    CONSTRAINT CK_fn_webhooks_key_rotation_State CHECK (PreviousVersion>=1 AND PreparedVersion=PreviousVersion+1 AND Id<>PreviousKeyId AND ((StateKey='prepared' AND ProtectedSecret IS NOT NULL AND CompletedAtUtc IS NULL AND CompletedVersion IS NULL AND CompletedByUserId IS NULL AND CompletedBySessionId IS NULL) OR (StateKey IN ('activated','cancelled') AND ProtectedSecret IS NULL AND CompletedAtUtc IS NOT NULL AND CompletedVersion>PreparedVersion AND CompletedByUserId IS NOT NULL AND CompletedBySessionId IS NOT NULL)))
) COMMENT='签名密钥准备及明确激活取消的同事务审计；完成后清除准备秘密';
DROP PROCEDURE IF EXISTS fn_webhooks_key_rotation_expand;
DELIMITER $$
CREATE PROCEDURE fn_webhooks_key_rotation_expand()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_webhooks_subscription' AND COLUMN_NAME='PreparedRotationId') THEN
        ALTER TABLE fn_webhooks_subscription ADD COLUMN PreparedRotationId binary(16) NULL COMMENT '当前唯一待处理轮换身份；完成后清空';
    ELSEIF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_webhooks_subscription' AND COLUMN_NAME='PreparedRotationId' AND COLUMN_COMMENT='') THEN
        ALTER TABLE fn_webhooks_subscription MODIFY COLUMN PreparedRotationId binary(16) NULL COMMENT '当前唯一待处理轮换身份；完成后清空';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_webhooks_key_rotation' AND INDEX_NAME='UX_fn_webhooks_key_rotation_TenantOperation') THEN
        CREATE UNIQUE INDEX UX_fn_webhooks_key_rotation_TenantOperation ON fn_webhooks_key_rotation(TenantId,OperationId);
    END IF;
END$$
DELIMITER ;
CALL fn_webhooks_key_rotation_expand();
DROP PROCEDURE IF EXISTS fn_webhooks_key_rotation_expand;
