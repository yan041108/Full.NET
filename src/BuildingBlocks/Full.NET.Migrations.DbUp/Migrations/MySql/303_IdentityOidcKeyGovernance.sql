-- 只登记公开材料与短期进程声明；DDL 重跑不重置活动、保留或退役状态。
CREATE TABLE IF NOT EXISTS fn_identity_oidc_signing_key (
    Id binary(16) NOT NULL COMMENT '应用端 UUID v7',
    KeyId varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '不可重新绑定材料的 kid',
    PublicKeyHash char(64) NOT NULL COMMENT '规范公钥 DER 的 SHA256',
    PublishedAtUtc datetime(6) NOT NULL COMMENT '公钥登记时间 UTC',
    LastActivatedAtUtc datetime(6) NULL COMMENT '最近激活时间 UTC',
    RetireAfterUtc datetime(6) NULL COMMENT '所有合法协议令牌覆盖后的最早退役时间 UTC',
    IsRetired tinyint(1) NOT NULL COMMENT '已退役标志且不可重新激活',
    Version int NOT NULL COMMENT '治理行并发版本',
    CONSTRAINT PK_fn_identity_oidc_signing_key PRIMARY KEY (Id),
    UNIQUE KEY UQ_fn_identity_oidc_signing_key_KeyId (KeyId)
) COMMENT='OIDC 签名公钥指纹与受控生命周期' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
CREATE TABLE IF NOT EXISTS fn_identity_oidc_signing_node (
    Id binary(16) NOT NULL COMMENT '进程独立 UUID v7，重启不得继承',
    RoleKey varchar(16) NOT NULL COMMENT '签名角色 api 或 worker',
    SigningManifest char(64) NOT NULL COMMENT '本节点全部公开签名材料摘要',
    EncryptionFingerprint char(64) NOT NULL COMMENT '共享协议加密与令牌期限配置摘要',
    PrivateKeysJson longtext NOT NULL COMMENT '本节点持有私钥的 kid 列表，不含私钥',
    PolicyVersion int NOT NULL COMMENT '本节点已观察的持久化策略版本',
    ExpiresAtUtc datetime(6) NOT NULL COMMENT '就绪租约到期时间 UTC',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '最近就绪确认时间 UTC',
    CONSTRAINT PK_fn_identity_oidc_signing_node PRIMARY KEY (Id),
    KEY IX_fn_identity_oidc_signing_node_Expires (ExpiresAtUtc)
) COMMENT='OIDC 签名进程的短期就绪租约' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
