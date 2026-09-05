using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>Workflow 办理人解析使用的角色与成员只读 SQL 集合。</summary>
internal static class WorkflowRoleMemberSql
{
    public static readonly SqlStatement CountActiveHostRoles = new(
        "identity.workflow.count_active_host_roles",
        """
        SELECT COUNT(1)
        FROM fn_identity_role
        WHERE ScopeKey = 'host'
          AND TenantId IS NULL
          AND IsActive = 1
          AND IsSuperAdministrator = 0
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListActiveHostRolesSqlServer = new(
        "identity.workflow.list_active_host_roles.sql_server",
        """
        SELECT Id, Code, Name
        FROM fn_identity_role
        WHERE ScopeKey = 'host'
          AND TenantId IS NULL
          AND IsActive = 1
          AND IsSuperAdministrator = 0
        ORDER BY Code
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListActiveHostRolesMySql = new(
        "identity.workflow.list_active_host_roles.mysql",
        """
        SELECT Id, Code, Name
        FROM fn_identity_role
        WHERE ScopeKey = 'host'
          AND TenantId IS NULL
          AND IsActive = 1
          AND IsSuperAdministrator = 0
        ORDER BY Code
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindActiveHostRolesByIds = new(
        "identity.workflow.find_active_host_roles_by_ids",
        """
        SELECT Id, Code, Name
        FROM fn_identity_role
        WHERE Id IN @RoleIds
          AND ScopeKey = 'host'
          AND TenantId IS NULL
          AND IsActive = 1
          AND IsSuperAdministrator = 0
        ORDER BY Code
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountActiveTenantRoles = new(
        "identity.workflow.count_active_tenant_roles",
        """
        SELECT COUNT(1)
        FROM fn_identity_role
        WHERE TenantId = @TenantId
          AND ScopeKey = @TenantScopeKey
          AND IsActive = 1
          AND IsSuperAdministrator = 0
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListActiveTenantRolesSqlServer = new(
        "identity.workflow.list_active_tenant_roles.sql_server",
        """
        SELECT Id, Code, Name
        FROM fn_identity_role
        WHERE TenantId = @TenantId
          AND ScopeKey = @TenantScopeKey
          AND IsActive = 1
          AND IsSuperAdministrator = 0
        ORDER BY Code
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListActiveTenantRolesMySql = new(
        "identity.workflow.list_active_tenant_roles.mysql",
        """
        SELECT Id, Code, Name
        FROM fn_identity_role
        WHERE TenantId = @TenantId
          AND ScopeKey = @TenantScopeKey
          AND IsActive = 1
          AND IsSuperAdministrator = 0
        ORDER BY Code
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindActiveTenantRolesByIds = new(
        "identity.workflow.find_active_tenant_roles_by_ids",
        """
        SELECT Id, Code, Name
        FROM fn_identity_role
        WHERE Id IN @RoleIds
          AND TenantId = @TenantId
          AND ScopeKey = @TenantScopeKey
          AND IsActive = 1
          AND IsSuperAdministrator = 0
        ORDER BY Code
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListActiveHostRoleMembersByRoleIds = new(
        "identity.workflow.list_active_host_role_members_by_role_ids",
        """
        SELECT userRole.RoleId, identityUser.Id AS UserId
        FROM fn_identity_user_role AS userRole
        INNER JOIN fn_identity_role AS roleObject
            ON roleObject.Id = userRole.RoleId
        INNER JOIN fn_identity_user AS identityUser
            ON identityUser.Id = userRole.UserId
        WHERE userRole.RoleId IN @RoleIds
          AND roleObject.ScopeKey = 'host'
          AND roleObject.TenantId IS NULL
          AND roleObject.IsActive = 1
          AND roleObject.IsSuperAdministrator = 0
          AND identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
          AND identityUser.IsActive = 1
        ORDER BY userRole.RoleId, identityUser.NormalizedUsername, identityUser.Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListActiveTenantRoleMembersByRoleIds = new(
        "identity.workflow.list_active_tenant_role_members_by_role_ids",
        """
        SELECT userRole.RoleId, identityUser.Id AS UserId
        FROM fn_identity_user_role AS userRole
        INNER JOIN fn_identity_role AS roleObject
            ON roleObject.Id = userRole.RoleId
        INNER JOIN fn_identity_user AS identityUser
            ON identityUser.Id = userRole.UserId
        WHERE userRole.RoleId IN @RoleIds
          AND roleObject.TenantId = @TenantId
          AND roleObject.ScopeKey = @TenantScopeKey
          AND roleObject.IsActive = 1
          AND roleObject.IsSuperAdministrator = 0
          AND identityUser.IsActive = 1
          AND
          (
              (identityUser.TenantId = @TenantId
               AND identityUser.ScopeKey = @TenantScopeKey)
              OR
              (
                  identityUser.TenantId IS NULL
                  AND identityUser.ScopeKey = 'host'
              )
          )
        ORDER BY userRole.RoleId, identityUser.NormalizedUsername, identityUser.Id
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
}

/// <summary>Workflow 角色目录列表行投影。</summary>
internal sealed record WorkflowRoleListRow(Guid Id, string Code, string Name);

/// <summary>Workflow 角色成员批量解析行投影。</summary>
internal sealed record WorkflowRoleMemberRow(Guid RoleId, Guid UserId);
