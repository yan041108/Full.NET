CREATE TABLE IF NOT EXISTS fn_identity_navigation
(
    Id BINARY(16) NOT NULL,
    TenantId BINARY(16) NULL,
    ScopeKey varchar(64) NOT NULL,
    ParentId BINARY(16) NULL,
    RouteName varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    Path varchar(256) NOT NULL,
    ComponentKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    Title varchar(128) NOT NULL,
    Caption varchar(256) NOT NULL,
    Icon varchar(64) NOT NULL,
    DisplayOrder int NOT NULL,
    RequiredPermission varchar(160) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    IsSystem boolean NOT NULL DEFAULT false,
    IsActive boolean NOT NULL DEFAULT true,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    CONSTRAINT PK_fn_identity_navigation PRIMARY KEY (Id),
    CONSTRAINT FK_fn_identity_navigation_Parent
        FOREIGN KEY (ParentId) REFERENCES fn_identity_navigation(Id),
    UNIQUE KEY UX_fn_identity_navigation_Scope_RouteName (ScopeKey, RouteName),
    KEY IX_fn_identity_navigation_Parent (ParentId, DisplayOrder)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
