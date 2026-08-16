CREATE TABLE IF NOT EXISTS fn_identity_navigation (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ScopeKey varchar(64) NOT NULL COMMENT '作用域键',
    ParentId BINARY(16) NULL COMMENT '父级标识',
    RouteName varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '路由名称',
    Path varchar(256) NOT NULL COMMENT '路由路径',
    ComponentKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '组件键',
    Title varchar(128) NOT NULL COMMENT '标题',
    Caption varchar(256) NOT NULL COMMENT '显示标题',
    Icon varchar(64) NOT NULL COMMENT '图标',
    DisplayOrder int NOT NULL COMMENT '显示顺序',
    RequiredPermission varchar(160) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '所需权限码',
    IsSystem boolean NOT NULL DEFAULT false COMMENT '是否系统内置',
    IsActive boolean NOT NULL DEFAULT true COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_identity_navigation PRIMARY KEY (Id),
    CONSTRAINT FK_fn_identity_navigation_Parent
        FOREIGN KEY (ParentId) REFERENCES fn_identity_navigation(Id),
    UNIQUE KEY UX_fn_identity_navigation_Scope_RouteName (ScopeKey, RouteName),
    KEY IX_fn_identity_navigation_Parent (ParentId, DisplayOrder)
) COMMENT='身份认证导航菜单表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
