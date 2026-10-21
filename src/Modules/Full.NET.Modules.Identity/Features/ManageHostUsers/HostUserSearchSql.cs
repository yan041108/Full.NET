using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Features.ManageHostUsers;

/// <summary>筛选仅连接 Identity 自有投影；手机号按当前字段揭示权限搜索。</summary>
internal sealed record HostUserListFilter(string? Username = null, string? DisplayName = null,
    string? Phone = null, string? Email = null, string? Status = null,
    Guid? OrganizationTenantId = null, Guid? OrganizationUnitId = null);

internal static class HostUserSearchSql
{
    private const string FromWhere = """
        FROM fn_identity_user users
        LEFT JOIN fn_identity_user_profile profile ON profile.UserId=users.Id
        WHERE users.ScopeKey='host' AND users.TenantId IS NULL
          AND (@Username IS NULL OR users.Username LIKE @Username ESCAPE '!')
          AND (@DisplayName IS NULL OR users.DisplayName LIKE @DisplayName ESCAPE '!')
          AND (@Email IS NULL OR profile.Email LIKE @Email ESCAPE '!')
          AND (@Phone IS NULL OR (profile.PhoneNumber IS NOT NULL AND (CASE WHEN @RevealPhone=1 THEN profile.PhoneNumber
                WHEN {length}(profile.PhoneNumber)<=4 THEN '****'
                ELSE CONCAT('****', RIGHT(profile.PhoneNumber,4)) END) LIKE @Phone ESCAPE '!'))
          AND (@IsActive IS NULL OR users.IsActive=@IsActive)
          AND (@OrganizationUnitId IS NULL OR EXISTS (
            SELECT 1 FROM fn_identity_organization_user_unit_projection assignment
            JOIN fn_identity_organization_unit_projection unit
              ON unit.TenantId=assignment.TenantId AND unit.UnitId=assignment.UnitId AND unit.IsActive=1
            WHERE assignment.TenantId=@OrganizationTenantId AND assignment.UnitId=@OrganizationUnitId
              AND assignment.UserId=users.Id AND assignment.IsActive=1))
        """;
    private const string Columns = "users.Id, users.Username, users.DisplayName, users.AccountType, users.IsActive, users.CreatedAtUtc, users.UpdatedAtUtc, users.RetiredAtUtc, users.Version";
    internal static readonly SqlStatement CountSqlServer = new("identity.host_users.count_filtered.sql_server",
        "SELECT COUNT(1) " + FromWhere.Replace("{length}", "LEN", StringComparison.Ordinal), SqlDataScope.HostOnly);
    internal static readonly SqlStatement CountMySql = new("identity.host_users.count_filtered.mysql",
        "SELECT COUNT(1) " + FromWhere.Replace("{length}", "CHAR_LENGTH", StringComparison.Ordinal), SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListSqlServer = new("identity.host_users.list_filtered.sql_server",
        "SELECT " + Columns + " " + FromWhere.Replace("{length}", "LEN", StringComparison.Ordinal)
        + " ORDER BY users.NormalizedUsername,users.Id OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListMySql = new("identity.host_users.list_filtered.mysql",
        "SELECT " + Columns + " " + FromWhere.Replace("{length}", "CHAR_LENGTH", StringComparison.Ordinal)
        + " ORDER BY users.NormalizedUsername,users.Id LIMIT @PageSize OFFSET @Offset", SqlDataScope.HostOnly);
}
