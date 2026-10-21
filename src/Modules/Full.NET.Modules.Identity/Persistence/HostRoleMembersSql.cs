using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>角色成员集合写入只触及 Identity 自有表，所有命令由调用方同一本地事务拥有。</summary>
internal static class HostRoleMembersSql
{
    public static readonly SqlStatement UpdateRoleVersion = new(
        "identity.host_role_members.update_role_version",
        """
        UPDATE fn_identity_role SET UpdatedAtUtc=@UpdatedAtUtc,Version=Version+1
        WHERE Id=@RoleId AND ScopeKey='host' AND TenantId IS NULL AND IsSystem=0
          AND IsSuperAdministrator=0 AND IsActive=1 AND Version=@Version
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement ValidateUsersSqlServer = new(
        "identity.host_role_members.validate_users.sqlserver",
        """
        SELECT u.Id,u.IsActive FROM fn_identity_user AS u WITH (UPDLOCK)
        WHERE u.ScopeKey='host' AND u.TenantId IS NULL
          AND u.Id IN (SELECT Id FROM OPENJSON(@UserIdsJson) WITH (Id uniqueidentifier '$'))
        ORDER BY u.Id
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement ValidateUsersMySql = new(
        "identity.host_role_members.validate_users.mysql",
        """
        SELECT u.Id,u.IsActive FROM fn_identity_user AS u
        WHERE u.ScopeKey='host' AND u.TenantId IS NULL
          AND u.Id IN (SELECT UUID_TO_BIN(j.Id,0) FROM JSON_TABLE(@UserIdsJson,'$[*]' COLUMNS(Id CHAR(36) PATH '$')) AS j)
        ORDER BY u.Id FOR UPDATE
        """, SqlDataScope.HostOnly);

    // 已有死锁图证明小表外键校验会扫描并锁住其他角色，与其用户锁形成环。
    // 仅限定本集合插入的连接策略，让外键按目标主键查找；不放松约束或增加全局互斥。
    public static readonly SqlStatement InsertMembersSqlServer = new(
        "identity.host_role_members.insert_members.sqlserver",
        """
        INSERT INTO fn_identity_user_role(UserId,RoleId)
        SELECT j.Id,@RoleId FROM OPENJSON(@UserIdsJson) WITH (Id uniqueidentifier '$') AS j
        OPTION (LOOP JOIN)
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertMembersMySql = new(
        "identity.host_role_members.insert_members.mysql",
        """
        INSERT INTO fn_identity_user_role(UserId,RoleId)
        SELECT UUID_TO_BIN(j.Id,0),@RoleId FROM JSON_TABLE(@UserIdsJson,'$[*]' COLUMNS(Id CHAR(36) PATH '$')) AS j
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement RotateStampsSqlServer = new(
        "identity.host_role_members.rotate_stamps.sqlserver",
        """
        UPDATE u SET SecurityStamp=j.SecurityStamp,UpdatedAtUtc=@Now,Version=u.Version+1
        FROM fn_identity_user AS u
        INNER JOIN OPENJSON(@UserStampsJson) WITH (UserId uniqueidentifier '$.userId',SecurityStamp nvarchar(32) '$.securityStamp') AS j ON j.UserId=u.Id
        WHERE u.ScopeKey='host' AND u.TenantId IS NULL
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement RotateStampsMySql = new(
        "identity.host_role_members.rotate_stamps.mysql",
        """
        UPDATE fn_identity_user AS u
        INNER JOIN JSON_TABLE(@UserStampsJson,'$[*]' COLUMNS(UserId CHAR(36) PATH '$.userId',SecurityStamp VARCHAR(32) PATH '$.securityStamp')) AS j ON UUID_TO_BIN(j.UserId,0)=u.Id
        SET u.SecurityStamp=j.SecurityStamp,u.UpdatedAtUtc=@Now,u.Version=u.Version+1
        WHERE u.ScopeKey='host' AND u.TenantId IS NULL
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeSessionsSqlServer = new(
        "identity.host_role_members.revoke_sessions.sqlserver",
        """
        UPDATE s SET RevokedAtUtc=@Now,Version=s.Version+1
        FROM fn_identity_refresh_session AS s
        INNER JOIN OPENJSON(@UserStampsJson) WITH (UserId uniqueidentifier '$.userId') AS j ON j.UserId=s.UserId
        WHERE s.RevokedAtUtc IS NULL
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeSessionsMySql = new(
        "identity.host_role_members.revoke_sessions.mysql",
        """
        UPDATE fn_identity_refresh_session AS s
        INNER JOIN JSON_TABLE(@UserStampsJson,'$[*]' COLUMNS(UserId CHAR(36) PATH '$.userId')) AS j ON UUID_TO_BIN(j.UserId,0)=s.UserId
        SET s.RevokedAtUtc=@Now,s.Version=s.Version+1 WHERE s.RevokedAtUtc IS NULL
        """, SqlDataScope.HostOnly);

}

/// <summary>应用端逐用户生成的安全戳，不使用数据库 UUID 或共享新戳。</summary>
internal sealed record HostRoleMemberStamp(Guid UserId, string SecurityStamp);

/// <summary>事务内锁定的新旧成员状态；仅用于活动用户验证。</summary>
internal sealed record HostRoleMemberUserState(Guid Id, bool IsActive);
