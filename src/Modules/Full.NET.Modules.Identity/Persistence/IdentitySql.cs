using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

internal static class IdentitySql
{
    public static readonly SqlStatement FindUserByScopeAndUsername = new(
        "identity.find_user_by_scope_and_username",
        """
        SELECT Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
               PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc,
               SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version,
               PreferredLocale, ProfileVersion
        FROM fn_identity_user
        WHERE ScopeKey = @ScopeKey AND NormalizedUsername = @NormalizedUsername
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindHostUserById = new(
        "identity.find_host_user_by_id",
        """
        SELECT Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
               PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc,
               SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version,
               PreferredLocale, ProfileVersion
        FROM fn_identity_user
        WHERE Id = @UserId AND ScopeKey = 'host' AND TenantId IS NULL
        """,
        // Global：供租户上下文中的 IHostUserDirectory 校验；SQL 仍限定 Host 行。
        SqlDataScope.Global);

    public static readonly SqlStatement ListHostUsersByIds = new(
        "identity.list_host_users_by_ids",
        """
        SELECT Id, Username, DisplayName
        FROM fn_identity_user
        WHERE Id IN @UserIds
          AND ScopeKey = 'host'
          AND TenantId IS NULL
        """,
        // Global：跨模块读取发生在租户上下文内，SQL 自身仍精确限制为 Host 用户。
        SqlDataScope.Global);

    public static readonly SqlStatement CountActiveHostUserSelections = new(
        "identity.count_active_host_user_selections",
        """
        SELECT COUNT(1)
        FROM fn_identity_user
        WHERE ScopeKey = 'host'
          AND TenantId IS NULL
          AND IsActive = 1
        """,
        // Global：租户业务只能读取显式限定为 Host 且活动的候选用户。
        SqlDataScope.Global);

    public static readonly SqlStatement ListActiveHostUserSelectionsSqlServer = new(
        "identity.list_active_host_user_selections.sql_server",
        """
        SELECT Id, Username, DisplayName
        FROM fn_identity_user
        WHERE ScopeKey = 'host'
          AND TenantId IS NULL
          AND IsActive = 1
        ORDER BY NormalizedUsername
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListActiveHostUserSelectionsMySql = new(
        "identity.list_active_host_user_selections.my_sql",
        """
        SELECT Id, Username, DisplayName
        FROM fn_identity_user
        WHERE ScopeKey = 'host'
          AND TenantId IS NULL
          AND IsActive = 1
        ORDER BY NormalizedUsername
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertUser = new(
        "identity.insert_user",
        """
        INSERT INTO fn_identity_user
            (Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
             PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc,
             SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version,
             PreferredLocale, ProfileVersion)
        VALUES
            (@Id, @TenantId, @ScopeKey, @Username, @NormalizedUsername, @DisplayName,
             @PasswordHash, @IsActive, @FailedLoginCount, @LockoutEndUtc,
             @SecurityStamp, @CreatedAtUtc, @UpdatedAtUtc, @Version,
             @PreferredLocale, @ProfileVersion)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountHostUsers = new(
        "identity.count_host_users",
        """
        SELECT COUNT(1)
        FROM fn_identity_user
        WHERE ScopeKey = 'host' AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostUsersSqlServer = new(
        "identity.list_host_users.sql_server",
        """
        SELECT Id, Username, DisplayName, IsActive, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_user
        WHERE ScopeKey = 'host' AND TenantId IS NULL
        ORDER BY NormalizedUsername
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostUsersMySql = new(
        "identity.list_host_users.mysql",
        """
        SELECT Id, Username, DisplayName, IsActive, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_user
        WHERE ScopeKey = 'host' AND TenantId IS NULL
        ORDER BY NormalizedUsername
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DisableHostUser = new(
        "identity.disable_host_user",
        """
        UPDATE fn_identity_user
        SET IsActive = 0,
            SecurityStamp = @SecurityStamp,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @UserId
          AND ScopeKey = 'host'
          AND TenantId IS NULL
          AND IsActive = 1
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement EnableHostUser = new(
        "identity.enable_host_user",
        """
        UPDATE fn_identity_user
        SET IsActive = 1,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @UserId
          AND ScopeKey = 'host'
          AND TenantId IS NULL
          AND IsActive = 0
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateHostUserDisplayName = new(
        "identity.update_host_user_display_name",
        """
        UPDATE fn_identity_user
        SET DisplayName = @DisplayName,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @UserId
          AND ScopeKey = 'host'
          AND TenantId IS NULL
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ResetHostUserPassword = new(
        "identity.reset_host_user_password",
        """
        UPDATE fn_identity_user
        SET PasswordHash = @PasswordHash,
            SecurityStamp = @SecurityStamp,
            FailedLoginCount = 0,
            LockoutEndUtc = NULL,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @UserId
          AND ScopeKey = 'host'
          AND TenantId IS NULL
          AND IsActive = 1
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindHostRoleById = new(
        "identity.find_host_role_by_id",
        """
        SELECT Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
               IsSuperAdministrator, DataScopeKind, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_role
        WHERE Id = @RoleId AND ScopeKey = 'host' AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountHostRoles = new(
        "identity.count_host_roles",
        """
        SELECT COUNT(1)
        FROM fn_identity_role
        WHERE ScopeKey = 'host' AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostRolesSqlServer = new(
        "identity.list_host_roles.sql_server",
        """
        SELECT Id, Code, Name, IsSystem, IsActive, IsSuperAdministrator,
               DataScopeKind, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_role
        WHERE ScopeKey = 'host' AND TenantId IS NULL
        ORDER BY Code
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostRolesMySql = new(
        "identity.list_host_roles.mysql",
        """
        SELECT Id, Code, Name, IsSystem, IsActive, IsSuperAdministrator,
               DataScopeKind, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_role
        WHERE ScopeKey = 'host' AND TenantId IS NULL
        ORDER BY Code
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateHostRoleName = new(
        "identity.update_host_role_name",
        """
        UPDATE fn_identity_role
        SET Name = @Name,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @RoleId
          AND ScopeKey = 'host'
          AND TenantId IS NULL
          AND IsSystem = 0
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DisableHostRole = new(
        "identity.disable_host_role",
        """
        UPDATE fn_identity_role
        SET IsActive = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @RoleId
          AND ScopeKey = 'host'
          AND TenantId IS NULL
          AND IsSystem = 0
          AND IsActive = 1
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteRolePermissions = new(
        "identity.delete_role_permissions",
        """
        DELETE FROM fn_identity_role_permission
        WHERE RoleId = @RoleId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateHostRoleVersion = new(
        "identity.update_host_role_version",
        """
        UPDATE fn_identity_role
        SET UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @RoleId
          AND ScopeKey = 'host'
          AND TenantId IS NULL
          AND IsSystem = 0
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindHostMenuById = new(
        "identity.find_host_menu_by_id",
        """
        SELECT Id, TenantId, ScopeKey, ParentId, RouteName, Path, ComponentKey,
               Title, Caption, Icon, DisplayOrder, RequiredPermission,
               IsSystem, IsActive, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_navigation
        WHERE Id = @MenuId AND ScopeKey = 'host' AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindHostMenuByScopeAndRouteName = new(
        "identity.find_host_menu_by_scope_and_route_name",
        """
        SELECT Id, TenantId, ScopeKey, ParentId, RouteName, Path, ComponentKey,
               Title, Caption, Icon, DisplayOrder, RequiredPermission,
               IsSystem, IsActive, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_navigation
        WHERE ScopeKey = @ScopeKey AND RouteName = @RouteName
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountHostMenus = new(
        "identity.count_host_menus",
        """
        SELECT COUNT(1)
        FROM fn_identity_navigation
        WHERE ScopeKey = 'host' AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostMenusSqlServer = new(
        "identity.list_host_menus.sql_server",
        """
        SELECT Id, ParentId, RouteName, Path, ComponentKey, Title, Caption, Icon,
               DisplayOrder, RequiredPermission, IsSystem, IsActive,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_navigation
        WHERE ScopeKey = 'host' AND TenantId IS NULL
        ORDER BY DisplayOrder, RouteName
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostMenusMySql = new(
        "identity.list_host_menus.mysql",
        """
        SELECT Id, ParentId, RouteName, Path, ComponentKey, Title, Caption, Icon,
               DisplayOrder, RequiredPermission, IsSystem, IsActive,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_navigation
        WHERE ScopeKey = 'host' AND TenantId IS NULL
        ORDER BY DisplayOrder, RouteName
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 列出活动 Host 菜单定义，供导航投影在任意请求上下文中合并代码目录。
    /// </summary>
    /// <remarks>
    /// 必须使用 <see cref="SqlDataScope.Global"/>：GetNavigation 在租户上下文中也会调用本语句；
    /// HostOnly 会触发 HostContextRequiredException，导致进入租户后会话快照加载失败并被客户端清空。
    /// SQL 本身仍限制 ScopeKey='host' 且 TenantId IS NULL，不读取租户业务行。
    /// </remarks>
    public static readonly SqlStatement ListActiveHostMenus = new(
        "identity.list_active_host_menus",
        """
        SELECT Id, TenantId, ScopeKey, ParentId, RouteName, Path, ComponentKey,
               Title, Caption, Icon, DisplayOrder, RequiredPermission,
               IsSystem, IsActive, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_navigation
        WHERE ScopeKey = 'host'
          AND TenantId IS NULL
          AND IsActive = 1
        ORDER BY DisplayOrder, RouteName
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertHostMenu = new(
        "identity.insert_host_menu",
        """
        INSERT INTO fn_identity_navigation
            (Id, TenantId, ScopeKey, ParentId, RouteName, Path, ComponentKey,
             Title, Caption, Icon, DisplayOrder, RequiredPermission,
             IsSystem, IsActive, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @ScopeKey, @ParentId, @RouteName, @Path, @ComponentKey,
             @Title, @Caption, @Icon, @DisplayOrder, @RequiredPermission,
             @IsSystem, @IsActive, @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateHostMenu = new(
        "identity.update_host_menu",
        """
        UPDATE fn_identity_navigation
        SET ParentId = @ParentId,
            Path = @Path,
            ComponentKey = @ComponentKey,
            Title = @Title,
            Caption = @Caption,
            Icon = @Icon,
            DisplayOrder = @DisplayOrder,
            RequiredPermission = @RequiredPermission,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @MenuId
          AND ScopeKey = 'host'
          AND TenantId IS NULL
          AND IsSystem = 0
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DisableHostMenu = new(
        "identity.disable_host_menu",
        """
        UPDATE fn_identity_navigation
        SET IsActive = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @MenuId
          AND ScopeKey = 'host'
          AND TenantId IS NULL
          AND IsActive = 1
        """,
        SqlDataScope.HostOnly);

    /// <summary>更新系统内置菜单的展示与层级字段；路由、组件与权限保持目录锁定。</summary>
    public static readonly SqlStatement UpdateHostSystemMenu = new(
        "identity.update_host_system_menu",
        """
        UPDATE fn_identity_navigation
        SET ParentId = @ParentId,
            Title = @Title,
            Caption = @Caption,
            Icon = @Icon,
            DisplayOrder = @DisplayOrder,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @MenuId
          AND ScopeKey = 'host'
          AND TenantId IS NULL
          AND IsSystem = 1
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostMenuRouteNames = new(
        "identity.list_host_menu_route_names",
        """
        SELECT Id, RouteName
        FROM fn_identity_navigation
        WHERE ScopeKey = 'host' AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindRefreshSessionById = new(
        "identity.find_refresh_session_by_explicit_session_id",
        """
        SELECT session.Id AS SessionId,
               session.UserId,
               session.FamilyId,
               session.ClientId,
               session.TokenHash,
               session.ExpiresAtUtc,
               session.ConsumedAtUtc,
               session.RevokedAtUtc,
               session.ReplacedById,
               session.ActiveTenantId,
               session.CreatedAtUtc,
               session.Version AS SessionVersion,
               identityUser.TenantId,
               identityUser.ScopeKey,
               identityUser.Username,
               identityUser.NormalizedUsername,
               identityUser.DisplayName,
               identityUser.PasswordHash,
               identityUser.IsActive,
               identityUser.FailedLoginCount,
               identityUser.LockoutEndUtc,
               identityUser.SecurityStamp,
               identityUser.CreatedAtUtc AS UserCreatedAtUtc,
               identityUser.UpdatedAtUtc AS UserUpdatedAtUtc,
               identityUser.Version AS UserVersion,
               identityUser.PreferredLocale,
               identityUser.ProfileVersion
        FROM fn_identity_refresh_session AS session
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = session.UserId
        WHERE session.Id = @SessionId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateRefreshSessionContext = new(
        "identity.update_refresh_session_explicit_context",
        """
        UPDATE fn_identity_refresh_session
        SET ActiveTenantId = @ActiveTenantId,
            Version = Version + 1
        WHERE Id = @SessionId
          AND UserId = @UserId
          AND Version = @Version
          AND ConsumedAtUtc IS NULL
          AND RevokedAtUtc IS NULL
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertContextAudit = new(
        "identity.insert_explicit_context_audit",
        """
        INSERT INTO fn_identity_auth_audit
            (Id, UserId, SessionId, UsernameFingerprint, EventType,
             ResultCode, Succeeded, IpAddress, UserAgent, ContextTenantId,
             OccurredAtUtc)
        VALUES
            (@Id, @UserId, @SessionId, @UsernameFingerprint, @EventType,
             @ResultCode, @Succeeded, @IpAddress, @UserAgent, @ContextTenantId,
             @OccurredAtUtc)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindRoleByScopeAndCode = new(
        "identity.find_role_by_scope_and_code",
        """
        SELECT Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
               IsSuperAdministrator, DataScopeKind,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_role
        WHERE ScopeKey = @ScopeKey AND Code = @Code
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertRole = new(
        "identity.insert_role",
        """
        INSERT INTO fn_identity_role
            (Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
             IsSuperAdministrator, DataScopeKind,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @ScopeKey, @Code, @Name, @IsSystem, @IsActive,
             @IsSuperAdministrator, @DataScopeKind,
             @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateSystemRole = new(
        "identity.update_system_role",
        """
        UPDATE fn_identity_role
        SET Name = @Name,
            IsSystem = 1,
            IsActive = 1,
            IsSuperAdministrator = @IsSuperAdministrator,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement GetRolePermissionCodes = new(
        "identity.get_role_permission_codes",
        """
        SELECT PermissionCode
        FROM fn_identity_role_permission
        WHERE RoleId = @RoleId
        ORDER BY PermissionCode
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement EnsureRolePermission = new(
        "identity.ensure_role_permission",
        """
        INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
        SELECT @RoleId, @PermissionCode
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM fn_identity_role_permission
            WHERE RoleId = @RoleId AND PermissionCode = @PermissionCode
        )
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement EnsureUserRole = new(
        "identity.ensure_user_role",
        """
        INSERT INTO fn_identity_user_role (UserId, RoleId)
        SELECT @UserId, @RoleId
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM fn_identity_user_role
            WHERE UserId = @UserId AND RoleId = @RoleId
        )
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement GetUserAssignableRoleIds = new(
        "identity.get_user_assignable_role_ids",
        """
        SELECT userRole.RoleId
        FROM fn_identity_user_role AS userRole
        INNER JOIN fn_identity_role AS roleObject
            ON roleObject.Id = userRole.RoleId
        WHERE userRole.UserId = @UserId
          AND roleObject.ScopeKey = 'host'
          AND roleObject.TenantId IS NULL
          AND roleObject.IsSystem = 0
          AND roleObject.IsSuperAdministrator = 0
        ORDER BY roleObject.Code
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement GetUserActiveRoleDataScopes = new(
        "identity.get_user_active_role_data_scopes",
        """
        SELECT roleObject.Id AS RoleId,
               roleObject.DataScopeKind,
               roleObject.IsSuperAdministrator
        FROM fn_identity_user_role AS userRole
        INNER JOIN fn_identity_role AS roleObject
            ON roleObject.Id = userRole.RoleId
        WHERE userRole.UserId = @UserId
          AND roleObject.ScopeKey = 'host'
          AND roleObject.TenantId IS NULL
          AND roleObject.IsActive = 1
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement DeleteUserAssignableRoles = new(
        "identity.delete_user_assignable_roles",
        """
        DELETE FROM fn_identity_user_role
        WHERE UserId = @UserId
          AND RoleId IN
          (
              SELECT roleObject.Id
              FROM fn_identity_role AS roleObject
              WHERE roleObject.ScopeKey = 'host'
                AND roleObject.TenantId IS NULL
                AND roleObject.IsSystem = 0
                AND roleObject.IsSuperAdministrator = 0
          )
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateHostUserRoleAssignments = new(
        "identity.update_host_user_role_assignments",
        """
        UPDATE fn_identity_user
        SET SecurityStamp = @SecurityStamp,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @UserId
          AND ScopeKey = 'host'
          AND TenantId IS NULL
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement LockSuperAdministratorRoleSqlServer = new(
        "identity.lock_super_administrator_role.sql_server",
        """
        SELECT Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
               IsSuperAdministrator, DataScopeKind, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_role WITH (UPDLOCK, HOLDLOCK)
        WHERE ScopeKey = 'host' AND Code = 'host-administrator'
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement LockSuperAdministratorRoleMySql = new(
        "identity.lock_super_administrator_role.my_sql",
        """
        SELECT Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
               IsSuperAdministrator, DataScopeKind, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_role
        WHERE ScopeKey = 'host' AND Code = 'host-administrator'
        FOR UPDATE
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountActiveSuperAdministratorAssignment = new(
        "identity.count_active_super_administrator_assignment",
        """
        SELECT COUNT(*)
        FROM fn_identity_user AS identityUser
        INNER JOIN fn_identity_user_role AS userRole
            ON userRole.UserId = identityUser.Id
        INNER JOIN fn_identity_role AS roleObject
            ON roleObject.Id = userRole.RoleId
        WHERE identityUser.Id = @UserId
          AND identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
          AND identityUser.IsActive = 1
          AND roleObject.ScopeKey = 'host'
          AND roleObject.TenantId IS NULL
          AND roleObject.IsActive = 1
          AND roleObject.IsSuperAdministrator = 1
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountActiveSuperAdministrators = new(
        "identity.count_active_super_administrators",
        """
        SELECT COUNT(*)
        FROM fn_identity_user AS identityUser
        INNER JOIN fn_identity_user_role AS userRole
            ON userRole.UserId = identityUser.Id
        INNER JOIN fn_identity_role AS roleObject
            ON roleObject.Id = userRole.RoleId
        WHERE identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
          AND identityUser.IsActive = 1
          AND roleObject.ScopeKey = 'host'
          AND roleObject.TenantId IS NULL
          AND roleObject.IsActive = 1
          AND roleObject.IsSuperAdministrator = 1
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountActiveHostUser = new(
        "identity.count_active_host_user",
        """
        SELECT COUNT(*)
        FROM fn_identity_user
        WHERE Id = @UserId
          AND ScopeKey = 'host'
          AND TenantId IS NULL
          AND IsActive = 1
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteSuperAdministratorAssignment = new(
        "identity.delete_super_administrator_assignment",
        """
        DELETE FROM fn_identity_user_role
        WHERE UserId = @UserId AND RoleId = @RoleId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListSuperAdministrators = new(
        "identity.list_super_administrators",
        """
        SELECT identityUser.Id AS UserId,
               identityUser.Username,
               identityUser.DisplayName,
               identityUser.IsActive
        FROM fn_identity_user AS identityUser
        INNER JOIN fn_identity_user_role AS userRole
            ON userRole.UserId = identityUser.Id
        INNER JOIN fn_identity_role AS roleObject
            ON roleObject.Id = userRole.RoleId
        WHERE identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
          AND roleObject.ScopeKey = 'host'
          AND roleObject.TenantId IS NULL
          AND roleObject.IsSuperAdministrator = 1
        ORDER BY identityUser.NormalizedUsername
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListSuperAdministratorAuditsSqlServer = new(
        "identity.list_super_administrator_audits.sql_server",
        """
        SELECT TOP (@Limit)
               Id, UserId AS TargetUserId, ActorUserId,
               EventType, ResultCode, Succeeded, OccurredAtUtc
        FROM fn_identity_auth_audit
        WHERE EventType IN
            ('identity.super_administrator.granted',
             'identity.super_administrator.revoked')
        ORDER BY OccurredAtUtc DESC, Id DESC
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListSuperAdministratorAuditsMySql = new(
        "identity.list_super_administrator_audits.mysql",
        """
        SELECT Id, UserId AS TargetUserId, ActorUserId,
               EventType, ResultCode, Succeeded, OccurredAtUtc
        FROM fn_identity_auth_audit
        WHERE EventType IN
            ('identity.super_administrator.granted',
             'identity.super_administrator.revoked')
        ORDER BY OccurredAtUtc DESC, Id DESC
        LIMIT @Limit
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertSuperAdministratorAudit = new(
        "identity.insert_super_administrator_audit",
        """
        INSERT INTO fn_identity_auth_audit
            (Id, UserId, SessionId, UsernameFingerprint, EventType,
             ResultCode, Succeeded, IpAddress, UserAgent, ContextTenantId,
             OccurredAtUtc, ActorUserId)
        VALUES
            (@Id, @UserId, @SessionId, @UsernameFingerprint, @EventType,
             @ResultCode, @Succeeded, @IpAddress, @UserAgent, @ContextTenantId,
             @OccurredAtUtc, @ActorUserId)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RotateSecurityStamp = new(
        "identity.rotate_security_stamp",
        """
        UPDATE fn_identity_user
        SET SecurityStamp = @SecurityStamp,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @UserId AND ScopeKey = 'host'
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeAllUserSessions = new(
        "identity.revoke_all_user_sessions",
        """
        UPDATE fn_identity_refresh_session
        SET RevokedAtUtc = @RevokedAtUtc,
            Version = Version + 1
        WHERE UserId = @UserId AND RevokedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeSessionsByRole = new(
        "identity.revoke_sessions_by_role",
        """
        UPDATE fn_identity_refresh_session
        SET RevokedAtUtc = @RevokedAtUtc,
            Version = Version + 1
        WHERE RevokedAtUtc IS NULL
          AND UserId IN
          (
              SELECT UserId
              FROM fn_identity_user_role
              WHERE RoleId = @RoleId
          )
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RotateSecurityStampsByRole = new(
        "identity.rotate_security_stamps_by_role",
        """
        UPDATE fn_identity_user
        SET SecurityStamp = @SecurityStamp,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE ScopeKey = 'host'
          AND Id IN
          (
              SELECT UserId
              FROM fn_identity_user_role
              WHERE RoleId = @RoleId
          )
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement GetUserAuthorization = new(
        "identity.get_actor_authorization",
        """
        SELECT rolePermission.PermissionCode,
               roleObject.IsSuperAdministrator
        FROM fn_identity_user_role AS userRole
        INNER JOIN fn_identity_role AS roleObject ON roleObject.Id = userRole.RoleId
        LEFT JOIN fn_identity_role_permission AS rolePermission
            ON rolePermission.RoleId = roleObject.Id
        WHERE userRole.UserId = @UserId
          AND roleObject.IsActive = 1
          AND roleObject.ScopeKey = @ScopeKey
          AND
          (
              (@ScopeKey = 'host' AND roleObject.TenantId IS NULL)
              OR (@ScopeKey <> 'host' AND roleObject.TenantId = @TenantId)
          )
        ORDER BY rolePermission.PermissionCode
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement GetUserFieldProjectionGrants = new(
        "identity.get_user_field_projection_grants",
        """
        SELECT roleObject.ScopeKey,
               roleObject.TenantId,
               roleObject.IsSuperAdministrator,
               fieldGrant.FieldKey
        FROM fn_identity_user AS identityUser
        INNER JOIN fn_identity_user_role AS userRole
            ON userRole.UserId = identityUser.Id
        INNER JOIN fn_identity_role AS roleObject
            ON roleObject.Id = userRole.RoleId
        LEFT JOIN fn_identity_role_field_grant AS fieldGrant
            ON fieldGrant.RoleId = roleObject.Id
           AND fieldGrant.ResourceKey = @ResourceKey
        WHERE identityUser.Id = @UserId
          AND identityUser.IsActive = 1
          AND roleObject.IsActive = 1
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement GetHostRoleFieldGrants = new(
        "identity.get_host_role_field_grants",
        """
        SELECT fieldGrant.FieldKey
        FROM fn_identity_role_field_grant AS fieldGrant
        INNER JOIN fn_identity_role AS roleObject
            ON roleObject.Id = fieldGrant.RoleId
        WHERE fieldGrant.RoleId = @RoleId
          AND fieldGrant.ResourceKey = @ResourceKey
          AND roleObject.ScopeKey = 'host'
          AND roleObject.TenantId IS NULL
        ORDER BY fieldGrant.FieldKey
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindHostUserProjectionBaseById = new(
        "identity.find_host_user_projection_base_by_id",
        """
        SELECT Id, Username, DisplayName, IsActive,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_user
        WHERE Id = @UserId AND ScopeKey = 'host' AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostUserProfilesByIds = new(
        "identity.list_host_user_profiles_by_ids",
        """
        SELECT UserId, Nickname, PhoneNumber, Email, EmployeeNumber, Gender,
               JoinDateUtc, SortOrder, IdCardType, IdCardNumber, BirthDate,
               Ethnicity, Address, GraduatedSchool, EducationLevel, PoliticalStatus,
               OfficePhone, EmergencyContact, EmergencyContactPhone,
               EmergencyContactAddress, Remark, Version
        FROM fn_identity_user_profile
        WHERE UserId IN @UserIds
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertHostUserProfile = new(
        "identity.insert_host_user_profile",
        """
        INSERT INTO fn_identity_user_profile
            (UserId, Nickname, PhoneNumber, Email, EmployeeNumber, Gender,
             JoinDateUtc, SortOrder, IdCardType, IdCardNumber, BirthDate,
             Ethnicity, Address, GraduatedSchool, EducationLevel, PoliticalStatus,
             OfficePhone, EmergencyContact, EmergencyContactPhone,
             EmergencyContactAddress, Remark, Version)
        VALUES
            (@UserId, @Nickname, @PhoneNumber, @Email, @EmployeeNumber, @Gender,
             @JoinDateUtc, @SortOrder, @IdCardType, @IdCardNumber, @BirthDate,
             @Ethnicity, @Address, @GraduatedSchool, @EducationLevel, @PoliticalStatus,
             @OfficePhone, @EmergencyContact, @EmergencyContactPhone,
             @EmergencyContactAddress, @Remark, 1)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateHostUserProfile = new(
        "identity.update_host_user_profile",
        """
        UPDATE fn_identity_user_profile
        SET Nickname = @Nickname,
            PhoneNumber = @PhoneNumber,
            Email = @Email,
            EmployeeNumber = @EmployeeNumber,
            Gender = @Gender,
            JoinDateUtc = @JoinDateUtc,
            SortOrder = @SortOrder,
            IdCardType = @IdCardType,
            IdCardNumber = @IdCardNumber,
            BirthDate = @BirthDate,
            Ethnicity = @Ethnicity,
            Address = @Address,
            GraduatedSchool = @GraduatedSchool,
            EducationLevel = @EducationLevel,
            PoliticalStatus = @PoliticalStatus,
            OfficePhone = @OfficePhone,
            EmergencyContact = @EmergencyContact,
            EmergencyContactPhone = @EmergencyContactPhone,
            EmergencyContactAddress = @EmergencyContactAddress,
            Remark = @Remark,
            Version = Version + 1
        WHERE UserId = @UserId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostUserPreferredLocales = new(
        "identity.list_host_user_preferred_locales",
        """
        SELECT Id, PreferredLocale AS Value
        FROM fn_identity_user
        WHERE Id IN @UserIds AND ScopeKey = 'host' AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostUserFailedLoginCounts = new(
        "identity.list_host_user_failed_login_counts",
        """
        SELECT Id, FailedLoginCount AS Value
        FROM fn_identity_user
        WHERE Id IN @UserIds AND ScopeKey = 'host' AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostUserLockoutEnds = new(
        "identity.list_host_user_lockout_ends",
        """
        SELECT Id, LockoutEndUtc AS Value
        FROM fn_identity_user
        WHERE Id IN @UserIds AND ScopeKey = 'host' AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteHostRoleFieldGrants = new(
        "identity.delete_host_role_field_grants",
        """
        DELETE FROM fn_identity_role_field_grant
        WHERE RoleId = @RoleId AND ResourceKey = @ResourceKey
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertHostRoleFieldGrant = new(
        "identity.insert_host_role_field_grant",
        """
        INSERT INTO fn_identity_role_field_grant
            (Id, RoleId, ResourceKey, FieldKey, CreatedAtUtc, CreatedById)
        VALUES
            (@Id, @RoleId, @ResourceKey, @FieldKey, @CreatedAtUtc, @CreatedById)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateLoginFailure = new(
        "identity.update_login_failure",
        """
        UPDATE fn_identity_user
        SET FailedLoginCount = @FailedLoginCount,
            LockoutEndUtc = @LockoutEndUtc,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateLoginSuccess = new(
        "identity.update_login_success",
        """
        UPDATE fn_identity_user
        SET PasswordHash = @PasswordHash,
            FailedLoginCount = 0,
            LockoutEndUtc = NULL,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertRefreshSession = new(
        "identity.insert_refresh_session",
        """
        INSERT INTO fn_identity_refresh_session
            (Id, UserId, FamilyId, ClientId, TokenHash, ExpiresAtUtc,
             ConsumedAtUtc, RevokedAtUtc, ReplacedById, ActiveTenantId,
             CreatedAtUtc, Version)
        VALUES
            (@Id, @UserId, @FamilyId, @ClientId, @TokenHash, @ExpiresAtUtc,
             @ConsumedAtUtc, @RevokedAtUtc, @ReplacedById, @ActiveTenantId,
             @CreatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertAuthAudit = new(
        "identity.insert_auth_audit",
        """
        INSERT INTO fn_identity_auth_audit
            (Id, UserId, SessionId, UsernameFingerprint, EventType,
             ResultCode, Succeeded, IpAddress, UserAgent, ContextTenantId,
             OccurredAtUtc)
        VALUES
            (@Id, @UserId, @SessionId, @UsernameFingerprint, @EventType,
             @ResultCode, @Succeeded, @IpAddress, @UserAgent, @ContextTenantId,
             @OccurredAtUtc)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountAuthenticationAudits = new(
        "identity.count_authentication_audits",
        "SELECT COUNT(*) FROM fn_identity_auth_audit",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindRefreshSessionByHash = new(
        "identity.find_refresh_session_by_hash",
        """
        SELECT session.Id AS SessionId,
               session.UserId,
               session.FamilyId,
               session.ClientId,
               session.TokenHash,
               session.ExpiresAtUtc,
               session.ConsumedAtUtc,
               session.RevokedAtUtc,
               session.ReplacedById,
               session.ActiveTenantId,
               session.CreatedAtUtc,
               session.Version AS SessionVersion,
               identityUser.TenantId,
               identityUser.ScopeKey,
               identityUser.Username,
               identityUser.NormalizedUsername,
               identityUser.DisplayName,
               identityUser.PasswordHash,
               identityUser.IsActive,
               identityUser.FailedLoginCount,
               identityUser.LockoutEndUtc,
               identityUser.SecurityStamp,
               identityUser.CreatedAtUtc AS UserCreatedAtUtc,
               identityUser.UpdatedAtUtc AS UserUpdatedAtUtc,
               identityUser.Version AS UserVersion,
               identityUser.PreferredLocale,
               identityUser.ProfileVersion
        FROM fn_identity_refresh_session AS session
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = session.UserId
        WHERE session.TokenHash = @TokenHash
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ConsumeRefreshSession = new(
        "identity.consume_refresh_session",
        """
        UPDATE fn_identity_refresh_session
        SET ConsumedAtUtc = @ConsumedAtUtc,
            ReplacedById = @ReplacedById,
            Version = Version + 1
        WHERE Id = @Id
          AND Version = @Version
          AND ConsumedAtUtc IS NULL
          AND RevokedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeRefreshFamily = new(
        "identity.revoke_refresh_family",
        """
        UPDATE fn_identity_refresh_session
        SET RevokedAtUtc = @RevokedAtUtc,
            Version = Version + 1
        WHERE FamilyId = @FamilyId AND RevokedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);

    // Global 查询仅接受 JWT 验证后的 sub 与演员原始作用域，两项必须同时命中。
    public static readonly SqlStatement FindProfileByIdentity = new(
        "identity.find_profile_by_verified_identity",
        """
        SELECT Id, ScopeKey, Username, DisplayName, IsActive,
               PreferredLocale, ProfileVersion
        FROM fn_identity_user
        WHERE Id = @UserId AND ScopeKey = @ScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateLocalePreference = new(
        "identity.update_locale_preference_by_verified_identity",
        """
        UPDATE fn_identity_user
        SET PreferredLocale = @PreferredLocale,
            ProfileVersion = ProfileVersion + 1
        WHERE Id = @UserId
          AND ScopeKey = @ScopeKey
          AND ProfileVersion = @ProfileVersion
          AND IsActive = 1
          AND SecurityStamp = @SecurityStamp
          AND EXISTS (
              SELECT 1
              FROM fn_identity_refresh_session AS session
              WHERE session.Id = @SessionId
                AND session.UserId = fn_identity_user.Id
                AND session.ExpiresAtUtc > @NowUtc
                AND session.ConsumedAtUtc IS NULL
                AND session.RevokedAtUtc IS NULL
          )
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement GetRoleDataScopeUnitIds = new(
        "identity.get_role_data_scope_unit_ids",
        """
        SELECT UnitId
        FROM fn_identity_role_data_scope_unit
        WHERE RoleId = @RoleId
        ORDER BY UnitId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteRoleDataScopeUnits = new(
        "identity.delete_role_data_scope_units",
        """
        DELETE FROM fn_identity_role_data_scope_unit
        WHERE RoleId = @RoleId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertRoleDataScopeUnit = new(
        "identity.insert_role_data_scope_unit",
        """
        INSERT INTO fn_identity_role_data_scope_unit (RoleId, UnitId)
        VALUES (@RoleId, @UnitId)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateRoleDataScopeKind = new(
        "identity.update_role_data_scope_kind",
        """
        UPDATE fn_identity_role
        SET DataScopeKind = @DataScopeKind,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @RoleId
          AND ScopeKey = 'host'
          AND TenantId IS NULL
          AND IsSystem = 0
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindUserTotpByUserId = new(
        "identity.find_user_totp_by_user_id",
        """
        SELECT UserId, SecretProtected, IsEnabled, ConfirmedAtUtc,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_user_totp
        WHERE UserId = @UserId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertUserTotpPending = new(
        "identity.insert_user_totp_pending",
        """
        INSERT INTO fn_identity_user_totp
            (UserId, SecretProtected, IsEnabled, ConfirmedAtUtc,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@UserId, @SecretProtected, 0, NULL,
             @CreatedAtUtc, @UpdatedAtUtc, 1)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ResetUserTotpPending = new(
        "identity.reset_user_totp_pending",
        """
        UPDATE fn_identity_user_totp
        SET SecretProtected = @SecretProtected,
            IsEnabled = 0,
            ConfirmedAtUtc = NULL,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE UserId = @UserId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ConfirmUserTotp = new(
        "identity.confirm_user_totp",
        """
        UPDATE fn_identity_user_totp
        SET IsEnabled = 1,
            ConfirmedAtUtc = @ConfirmedAtUtc,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE UserId = @UserId
          AND IsEnabled = 0
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);
}

internal sealed record ConsumeRefreshSessionUpdate(
    Guid Id,
    DateTimeOffset ConsumedAtUtc,
    Guid ReplacedById,
    int Version);

internal sealed record LoginFailureUpdate(
    Guid Id,
    int FailedLoginCount,
    DateTimeOffset? LockoutEndUtc,
    DateTimeOffset UpdatedAtUtc,
    int Version);

internal sealed record LoginSuccessUpdate(
    Guid Id,
    string PasswordHash,
    DateTimeOffset UpdatedAtUtc,
    int Version);
