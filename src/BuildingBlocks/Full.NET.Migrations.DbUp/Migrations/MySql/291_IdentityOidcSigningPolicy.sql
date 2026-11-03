-- MySQL DDL 可独立提交，结构存在时重跑不覆盖权威指针。
CREATE TABLE IF NOT EXISTS fn_identity_oidc_signing_policy (
    Id binary(16) NOT NULL COMMENT '应用端 UUID v7',
    PolicyKey varchar(32) NOT NULL COMMENT '固定 default 策略标识',
    ActiveKeyId varchar(128) NOT NULL COMMENT '当前用于签发的密钥标识，不包含秘密',
    Version int NOT NULL COMMENT '持久化策略版本',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '最近激活时间',
    CONSTRAINT PK_fn_identity_oidc_signing_policy PRIMARY KEY (Id),
    CONSTRAINT UQ_fn_identity_oidc_signing_policy_key UNIQUE (PolicyKey)
) COMMENT='OIDC 集群活动签名策略';
