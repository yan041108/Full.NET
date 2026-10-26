using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>发起人自选的角色范围按实际成员并集分页，角色和用户均受当前可信作用域约束。</summary>
internal static class WorkflowRoleSelectionSql
{
    private const string Host = """
        FROM fn_identity_user AS candidate
        WHERE candidate.ScopeKey='host' AND candidate.TenantId IS NULL AND candidate.IsActive=1
          AND EXISTS (SELECT 1 FROM fn_identity_user_role AS membership
            INNER JOIN fn_identity_role AS roleObject ON roleObject.Id=membership.RoleId
            WHERE membership.UserId=candidate.Id AND membership.RoleId IN @RoleIds
              AND roleObject.ScopeKey='host' AND roleObject.TenantId IS NULL AND roleObject.IsActive=1 AND roleObject.IsSuperAdministrator=0)
        """;
    private const string Tenant = """
        FROM fn_identity_user AS candidate
        INNER JOIN fn_identity_tenant_member AS tenantMember ON tenantMember.UserId=candidate.Id AND tenantMember.TenantId=@TenantId AND tenantMember.Status='Active'
        WHERE candidate.IsActive=1 AND ((candidate.TenantId=@TenantId AND candidate.ScopeKey=@TenantScopeKey)
            OR (candidate.TenantId IS NULL AND candidate.ScopeKey='host'))
          AND EXISTS (SELECT 1 FROM fn_identity_user_role AS membership
            INNER JOIN fn_identity_role AS roleObject ON roleObject.Id=membership.RoleId
            WHERE membership.UserId=candidate.Id AND membership.RoleId IN @RoleIds
              AND roleObject.TenantId=@TenantId AND roleObject.ScopeKey=@TenantScopeKey AND roleObject.IsActive=1 AND roleObject.IsSuperAdministrator=0)
        """;
    private const string Projection = "SELECT candidate.Id, candidate.Username, COALESCE(candidate.DisplayName,candidate.Username) AS DisplayName ";
    private const string SqlServerPage = " ORDER BY candidate.Username, candidate.Id OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
    private const string MySqlPage = " ORDER BY candidate.Username, candidate.Id LIMIT @PageSize OFFSET @Offset";
    public static readonly SqlStatement CountHost = new("identity.workflow.selection_members.count.host", "SELECT COUNT(*) " + Host, SqlDataScope.HostOnly);
    public static readonly SqlStatement CountTenant = new("identity.workflow.selection_members.count.tenant", "SELECT COUNT(*) " + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement ListHostSqlServer = new("identity.workflow.selection_members.list.host.sqlserver", Projection + Host + SqlServerPage, SqlDataScope.HostOnly);
    public static readonly SqlStatement ListHostMySql = new("identity.workflow.selection_members.list.host.mysql", Projection + Host + MySqlPage, SqlDataScope.HostOnly);
    public static readonly SqlStatement ListTenantSqlServer = new("identity.workflow.selection_members.list.tenant.sqlserver", Projection + Tenant + SqlServerPage, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement ListTenantMySql = new("identity.workflow.selection_members.list.tenant.mysql", Projection + Tenant + MySqlPage, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}

internal sealed record WorkflowRoleUserRow(Guid Id, string Username, string DisplayName);
