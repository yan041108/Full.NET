-- 084：Identity 消费 Organization 机构单元的本地投影表。

IF OBJECT_ID(N'dbo.fn_identity_organization_unit_projection', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_organization_unit_projection
    (
        TenantId uniqueidentifier NOT NULL,
        UnitId uniqueidentifier NOT NULL,
        Name nvarchar(128) NOT NULL,
        IsActive bit NOT NULL,
        SourceVersion bigint NOT NULL,
        SourceUpdatedAtUtc datetimeoffset(7) NOT NULL,
        ProjectedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_identity_organization_unit_projection
            PRIMARY KEY CLUSTERED (TenantId, UnitId)
    );
END;
