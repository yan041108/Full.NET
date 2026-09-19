-- 228: demo enterprise request header + line tables (F09 showcase).
IF OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.demo_enterprise_request_enterprise_request
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        OrganizationUnitId uniqueidentifier NOT NULL,
        RequestNumber nvarchar(64) NOT NULL,
        Title nvarchar(200) NOT NULL,
        Status nvarchar(32) NOT NULL,
        TotalAmount decimal(18, 2) NOT NULL,
        ApplicantUserId uniqueidentifier NOT NULL,
        Version bigint NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CreatedById uniqueidentifier NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        UpdatedById uniqueidentifier NULL,
        IsDeleted bit NOT NULL,
        DeletedAtUtc datetimeoffset(7) NULL,
        DeletedById uniqueidentifier NULL,
        CONSTRAINT PK_demo_enterprise_request_enterprise_request PRIMARY KEY NONCLUSTERED (Id)
    );
    CREATE CLUSTERED INDEX IX_demo_enterprise_request_enterprise_request_TenantId_Id
        ON dbo.demo_enterprise_request_enterprise_request(TenantId, Id);
END;

IF OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.demo_enterprise_request_enterprise_request_line
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        RequestId uniqueidentifier NOT NULL,
        LineNumber int NOT NULL,
        ItemDescription nvarchar(200) NOT NULL,
        Quantity decimal(18, 4) NOT NULL,
        UnitPrice decimal(18, 2) NOT NULL,
        LineAmount decimal(18, 2) NOT NULL,
        Version bigint NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CreatedById uniqueidentifier NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        UpdatedById uniqueidentifier NULL,
        IsDeleted bit NOT NULL,
        DeletedAtUtc datetimeoffset(7) NULL,
        DeletedById uniqueidentifier NULL,
        CONSTRAINT PK_demo_enterprise_request_enterprise_request_line PRIMARY KEY NONCLUSTERED (Id)
    );
    CREATE CLUSTERED INDEX IX_demo_enterprise_request_enterprise_request_line_TenantId_RequestId
        ON dbo.demo_enterprise_request_enterprise_request_line(TenantId, RequestId);
END;
