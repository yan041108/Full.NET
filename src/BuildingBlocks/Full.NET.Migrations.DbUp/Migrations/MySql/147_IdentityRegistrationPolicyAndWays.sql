-- 147：注册策略单例与租户注册方式主表。

CREATE TABLE IF NOT EXISTS fn_identity_registration_policy (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    IsPublicRegistrationEnabled tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否允许公开注册入口',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_identity_registration_policy PRIMARY KEY (Id)
) COMMENT='用户注册策略单例表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO fn_identity_registration_policy
    (Id, IsPublicRegistrationEnabled, UpdatedAtUtc, Version)
SELECT UUID_TO_BIN('00000000-0000-4000-8000-000000000001', 0), 0, UTC_TIMESTAMP(6), 1
WHERE NOT EXISTS (
    SELECT 1
    FROM fn_identity_registration_policy
    WHERE Id = UUID_TO_BIN('00000000-0000-4000-8000-000000000001', 0)
);

CREATE TABLE IF NOT EXISTS fn_identity_user_registration_way (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '所属租户标识',
    Name varchar(128) NOT NULL COMMENT '显示名称',
    Code varchar(64) NOT NULL COMMENT '稳定机器码',
    IsEnabled tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    RoleId BINARY(16) NOT NULL COMMENT '默认角色标识',
    OrganizationUnitId BINARY(16) NOT NULL COMMENT '默认机构单元标识',
    PositionId BINARY(16) NULL COMMENT '默认职位标识',
    SortOrder int NOT NULL DEFAULT 0 COMMENT '排序序号',
    Remark varchar(256) NULL COMMENT '管理员备注',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_identity_user_registration_way PRIMARY KEY (Id),
    CONSTRAINT UX_fn_identity_user_registration_way_TenantId_Name UNIQUE (TenantId, Name),
    CONSTRAINT UX_fn_identity_user_registration_way_TenantId_Code UNIQUE (TenantId, Code),
    CONSTRAINT FK_fn_identity_user_registration_way_Role
        FOREIGN KEY (RoleId) REFERENCES fn_identity_role (Id),
    KEY IX_fn_identity_user_registration_way_TenantId_SortOrder (TenantId, SortOrder, Name, Id)
) COMMENT='租户用户注册方式表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
