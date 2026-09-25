-- 235：单条 InnoDB 原子 DDL 扩展认证事件上下文；失败不留下部分新增列。
ALTER TABLE fn_identity_auth_audit
    DROP FOREIGN KEY FK_fn_identity_auth_audit_User,
    ADD COLUMN TraceId varchar(64) NULL COMMENT '请求追踪标识',
    ADD COLUMN AuthenticationMethod varchar(40) NULL COMMENT '认证方式',
    ADD COLUMN ClientId varchar(128) NULL COMMENT 'OIDC 客户端标识',
    ADD COLUMN CenterSessionId binary(16) NULL COMMENT '中心会话标识',
    ADD COLUMN ApplicationSessionId binary(16) NULL COMMENT '应用会话标识';
