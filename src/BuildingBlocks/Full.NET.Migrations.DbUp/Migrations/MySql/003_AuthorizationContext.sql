CREATE TABLE IF NOT EXISTS fn_identity_role
(
    Id char(36) NOT NULL,
    TenantId char(36) NULL,
    ScopeKey varchar(64) NOT NULL,
    Code varchar(160) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    Name varchar(128) NOT NULL,
    IsSystem boolean NOT NULL,
    IsActive boolean NOT NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    CONSTRAINT PK_fn_identity_role PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_identity_role_Scope_Code (ScopeKey, Code),
    KEY IX_fn_identity_role_Tenant (TenantId, IsActive)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_identity_user_role
(
    UserId char(36) NOT NULL,
    RoleId char(36) NOT NULL,
    CONSTRAINT PK_fn_identity_user_role PRIMARY KEY (UserId, RoleId),
    CONSTRAINT FK_fn_identity_user_role_User
        FOREIGN KEY (UserId) REFERENCES fn_identity_user(Id),
    CONSTRAINT FK_fn_identity_user_role_Role
        FOREIGN KEY (RoleId) REFERENCES fn_identity_role(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_identity_role_permission
(
    RoleId char(36) NOT NULL,
    PermissionCode varchar(160) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    CONSTRAINT PK_fn_identity_role_permission PRIMARY KEY (RoleId, PermissionCode),
    CONSTRAINT FK_fn_identity_role_permission_Role
        FOREIGN KEY (RoleId) REFERENCES fn_identity_role(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

ALTER TABLE fn_identity_refresh_session
    ADD COLUMN ActiveTenantId char(36) NULL;

ALTER TABLE fn_identity_auth_audit
    ADD COLUMN ContextTenantId char(36) NULL;
