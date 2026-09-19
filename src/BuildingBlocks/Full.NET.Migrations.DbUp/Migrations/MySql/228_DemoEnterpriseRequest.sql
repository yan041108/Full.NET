-- 228: demo enterprise request header + line tables (F09 showcase).
CREATE TABLE IF NOT EXISTS demo_enterprise_request_enterprise_request
(
    Id BINARY(16) NOT NULL,
    TenantId BINARY(16) NOT NULL,
    OrganizationUnitId BINARY(16) NOT NULL,
    RequestNumber varchar(64) NOT NULL,
    Title varchar(200) NOT NULL,
    Status varchar(32) NOT NULL,
    TotalAmount decimal(18, 2) NOT NULL,
    ApplicantUserId BINARY(16) NOT NULL,
    Version bigint NOT NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    CreatedById BINARY(16) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    UpdatedById BINARY(16) NULL,
    IsDeleted boolean NOT NULL,
    DeletedAtUtc datetime(6) NULL,
    DeletedById BINARY(16) NULL,
    CONSTRAINT PK_demo_enterprise_request_enterprise_request PRIMARY KEY (Id),
    KEY IX_demo_enterprise_request_enterprise_request_TenantId_Id (TenantId, Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS demo_enterprise_request_enterprise_request_line
(
    Id BINARY(16) NOT NULL,
    TenantId BINARY(16) NOT NULL,
    RequestId BINARY(16) NOT NULL,
    LineNumber int NOT NULL,
    ItemDescription varchar(200) NOT NULL,
    Quantity decimal(18, 4) NOT NULL,
    UnitPrice decimal(18, 2) NOT NULL,
    LineAmount decimal(18, 2) NOT NULL,
    Version bigint NOT NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    CreatedById BINARY(16) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    UpdatedById BINARY(16) NULL,
    IsDeleted boolean NOT NULL,
    DeletedAtUtc datetime(6) NULL,
    DeletedById BINARY(16) NULL,
    CONSTRAINT PK_demo_enterprise_request_enterprise_request_line PRIMARY KEY (Id),
    KEY IX_demo_enterprise_request_enterprise_request_line_TenantId_RequestId (TenantId, RequestId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
