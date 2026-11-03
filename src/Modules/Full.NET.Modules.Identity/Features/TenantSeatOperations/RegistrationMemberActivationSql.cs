using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Features.TenantSeatOperations;

/// <summary>激活阶段只授予同租户的活动业务角色，绝不触及 Host 或超级管理员角色。</summary>
internal static class RegistrationMemberActivationSql
{
    internal static readonly SqlStatement GrantRole = new("identity.registration_member.grant_role", """
        INSERT INTO fn_identity_user_role (UserId, RoleId)
        SELECT @UserId, roleObject.Id FROM fn_identity_role AS roleObject
        WHERE roleObject.Id=@RoleId AND roleObject.TenantId=@TenantId
          AND roleObject.ScopeKey=@ScopeKey AND roleObject.IsActive=1 AND roleObject.IsSuperAdministrator=0
          AND EXISTS (SELECT 1 FROM fn_identity_user_registration_way WHERE Id=@WayId AND TenantId=@TenantId AND Version=@WayVersion AND IsEnabled=1)
          AND NOT EXISTS (SELECT 1 FROM fn_identity_user_role WHERE UserId=@UserId AND RoleId=@RoleId)
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HasRole = new("identity.registration_member.has_role", """
        SELECT COUNT(1) FROM fn_identity_user_role AS assignment
        INNER JOIN fn_identity_role AS roleObject ON roleObject.Id=assignment.RoleId
        WHERE assignment.UserId=@UserId AND assignment.RoleId=@RoleId AND roleObject.TenantId=@TenantId
          AND roleObject.ScopeKey=@ScopeKey AND roleObject.IsActive=1 AND roleObject.IsSuperAdministrator=0
          AND EXISTS (SELECT 1 FROM fn_identity_user_registration_way WHERE Id=@WayId AND TenantId=@TenantId AND Version=@WayVersion AND IsEnabled=1)
        """, SqlDataScope.HostOnly);
}
