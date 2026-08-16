CREATE TABLE IF NOT EXISTS fn_identity_role (
    Id char(36) NOT NULL COMMENT '逻辑主键',
    TenantId char(36) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ScopeKey varchar(64) NOT NULL COMMENT '作用域键',
    Code varchar(160) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '编码',
    Name varchar(128) NOT NULL COMMENT '名称',
    IsSystem boolean NOT NULL COMMENT '是否系统内置',
    IsActive boolean NOT NULL COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_identity_role PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_identity_role_Scope_Code (ScopeKey, Code),
    KEY IX_fn_identity_role_Tenant (TenantId, IsActive)
) COMMENT='身份认证角色表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_identity_user_role (
    UserId char(36) NOT NULL COMMENT '用户标识',
    RoleId char(36) NOT NULL COMMENT '角色标识',
    CONSTRAINT PK_fn_identity_user_role PRIMARY KEY (UserId, RoleId),
    CONSTRAINT FK_fn_identity_user_role_User
        FOREIGN KEY (UserId) REFERENCES fn_identity_user(Id),
    CONSTRAINT FK_fn_identity_user_role_Role
        FOREIGN KEY (RoleId) REFERENCES fn_identity_role(Id)
) COMMENT='身份认证用户角色表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_identity_role_permission (
    RoleId char(36) NOT NULL COMMENT '角色标识',
    PermissionCode varchar(160) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '权限码',
    CONSTRAINT PK_fn_identity_role_permission PRIMARY KEY (RoleId, PermissionCode),
    CONSTRAINT FK_fn_identity_role_permission_Role
        FOREIGN KEY (RoleId) REFERENCES fn_identity_role(Id)
) COMMENT='身份认证角色权限表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

ALTER TABLE fn_identity_refresh_session
    ADD ActiveTenantId char(36) NULL COMMENT '当前活动租户标识';

ALTER TABLE fn_identity_auth_audit
    ADD ContextTenantId char(36) NULL COMMENT '上下文租户标识';
