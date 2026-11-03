-- 票据只有密文与句柄摘要；DDL 重跑不得覆盖已有票据。
CREATE TABLE IF NOT EXISTS fn_identity_browser_session (
    Id binary(16) NOT NULL COMMENT '应用端 UUID v7',
    HandleHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '随机 Cookie 句柄的 SHA256 摘要',
    Ciphertext longtext NOT NULL COMMENT 'Data Protection 保护的令牌与防伪载荷',
    ExpiresAtUtc datetime(6) NOT NULL COMMENT '浏览器会话绝对过期时间',
    Version bigint NOT NULL COMMENT '票据凭据版本',
    RefreshLeaseId binary(16) NULL COMMENT '刷新或上下文变更租约标识',
    RefreshLeaseUntilUtc datetime(6) NULL COMMENT '租约过期时间，过期后必须重新登录',
    CONSTRAINT PK_fn_identity_browser_session PRIMARY KEY (Id),
    CONSTRAINT UQ_fn_identity_browser_session_handle UNIQUE (HandleHash),
    INDEX IX_fn_identity_browser_session_expires (ExpiresAtUtc)
) COMMENT='OIDC 浏览器服务端票据与刷新租约';
