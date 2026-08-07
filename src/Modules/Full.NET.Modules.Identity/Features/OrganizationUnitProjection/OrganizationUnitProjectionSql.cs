using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Features.OrganizationUnitProjection;

internal static class OrganizationUnitProjectionSql
{
    public static readonly SqlStatement FindActiveByTenantAndUnit = new(
        "identity.organization_unit_projection.find_active_by_tenant_and_unit",
        """
        SELECT UnitId, Name, IsActive, SourceVersion
        FROM fn_identity_organization_unit_projection
        WHERE TenantId = @TenantId
          AND UnitId = @UnitId
          AND IsActive = 1
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateIfNewer = new(
        "identity.organization_unit_projection.update_if_newer",
        """
        UPDATE fn_identity_organization_unit_projection
        SET Name = @Name,
            IsActive = @IsActive,
            SourceVersion = @SourceVersion,
            SourceUpdatedAtUtc = @SourceUpdatedAtUtc,
            ProjectedAtUtc = @ProjectedAtUtc
        WHERE TenantId = @TenantId
          AND UnitId = @UnitId
          AND SourceVersion < @SourceVersion
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertIfMissing = new(
        "identity.organization_unit_projection.insert_if_missing",
        """
        INSERT INTO fn_identity_organization_unit_projection
            (TenantId, UnitId, Name, IsActive, SourceVersion,
             SourceUpdatedAtUtc, ProjectedAtUtc)
        SELECT @TenantId, @UnitId, @Name, @IsActive, @SourceVersion,
               @SourceUpdatedAtUtc, @ProjectedAtUtc
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_identity_organization_unit_projection
            WHERE TenantId = @TenantId
              AND UnitId = @UnitId)
        """,
        SqlDataScope.Global);
}
