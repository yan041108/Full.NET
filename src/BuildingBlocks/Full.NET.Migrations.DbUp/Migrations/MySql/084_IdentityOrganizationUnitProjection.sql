CREATE TABLE IF NOT EXISTS fn_identity_organization_unit_projection
(
    TenantId BINARY(16) NOT NULL,
    UnitId BINARY(16) NOT NULL,
    Name varchar(128) NOT NULL,
    IsActive boolean NOT NULL,
    SourceVersion bigint NOT NULL,
    SourceUpdatedAtUtc datetime(6) NOT NULL,
    ProjectedAtUtc datetime(6) NOT NULL,
    PRIMARY KEY (TenantId, UnitId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
