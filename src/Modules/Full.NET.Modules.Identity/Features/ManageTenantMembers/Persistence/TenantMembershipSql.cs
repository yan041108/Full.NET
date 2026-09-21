using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;

internal static class TenantMembershipSql
{
    public static readonly SqlStatement CountMembers = new(
        "identity.tenant_members.count",
        """
        SELECT COUNT(1)
        FROM fn_identity_tenant_member
        WHERE TenantId = @TenantId
          AND (@Status IS NULL OR Status = @Status)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListMembersSqlServer = new(
        "identity.tenant_members.list.sql_server",
        """
        SELECT member.Id,
               member.TenantId,
               member.UserId,
               userAccount.Username,
               COALESCE(userAccount.DisplayName, userAccount.Username) AS DisplayName,
               member.MemberRole,
               member.Status,
               member.CreatedAtUtc,
               member.UpdatedAtUtc,
               member.Version
        FROM fn_identity_tenant_member AS member
        INNER JOIN fn_identity_user AS userAccount ON userAccount.Id = member.UserId
        WHERE member.TenantId = @TenantId
          AND (@Status IS NULL OR member.Status = @Status)
        ORDER BY member.CreatedAtUtc DESC, member.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListMembersMySql = new(
        "identity.tenant_members.list.mysql",
        """
        SELECT member.Id,
               member.TenantId,
               member.UserId,
               userAccount.Username,
               COALESCE(userAccount.DisplayName, userAccount.Username) AS DisplayName,
               member.MemberRole,
               member.Status,
               member.CreatedAtUtc,
               member.UpdatedAtUtc,
               member.Version
        FROM fn_identity_tenant_member AS member
        INNER JOIN fn_identity_user AS userAccount ON userAccount.Id = member.UserId
        WHERE member.TenantId = @TenantId
          AND (@Status IS NULL OR member.Status = @Status)
        ORDER BY member.CreatedAtUtc DESC, member.Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindMemberById = new(
        "identity.tenant_members.find_by_id",
        """
        SELECT member.Id,
               member.TenantId,
               member.UserId,
               userAccount.Username,
               COALESCE(userAccount.DisplayName, userAccount.Username) AS DisplayName,
               member.MemberRole,
               member.Status,
               member.CreatedAtUtc,
               member.UpdatedAtUtc,
               member.Version
        FROM fn_identity_tenant_member AS member
        INNER JOIN fn_identity_user AS userAccount ON userAccount.Id = member.UserId
        WHERE member.Id = @MemberId
          AND member.TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindMemberByTenantAndUser = new(
        "identity.tenant_members.find_by_tenant_user",
        """
        SELECT Id, TenantId, UserId, MemberRole, Status, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_tenant_member
        WHERE TenantId = @TenantId AND UserId = @UserId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement InsertMember = new(
        "identity.tenant_members.insert",
        """
        INSERT INTO fn_identity_tenant_member
            (Id, TenantId, UserId, MemberRole, Status, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @UserId, @MemberRole, @Status, @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateMember = new(
        "identity.tenant_members.update",
        """
        UPDATE fn_identity_tenant_member
        SET MemberRole = @MemberRole,
            Status = @Status,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @MemberId
          AND TenantId = @TenantId
          AND Version = @Version
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement CountActiveAdmins = new(
        "identity.tenant_members.count_active_admins",
        """
        SELECT COUNT(1)
        FROM fn_identity_tenant_member
        WHERE TenantId = @TenantId
          AND Status = @ActiveStatus
          AND MemberRole IN (@OwnerRole, @AdminRole)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement CountInvitations = new(
        "identity.tenant_invitations.count",
        """
        SELECT COUNT(1)
        FROM fn_identity_tenant_invitation
        WHERE TenantId = @TenantId
          AND (@Status IS NULL OR Status = @Status)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListInvitationsSqlServer = new(
        "identity.tenant_invitations.list.sql_server",
        """
        SELECT Id, TenantId, TargetEmail, TargetUserId, InvitedByUserId,
               MemberRole, TokenHash, Status, ExpiresAtUtc, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_tenant_invitation
        WHERE TenantId = @TenantId
          AND (@Status IS NULL OR Status = @Status)
        ORDER BY CreatedAtUtc DESC, Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListInvitationsMySql = new(
        "identity.tenant_invitations.list.mysql",
        """
        SELECT Id, TenantId, TargetEmail, TargetUserId, InvitedByUserId,
               MemberRole, TokenHash, Status, ExpiresAtUtc, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_tenant_invitation
        WHERE TenantId = @TenantId
          AND (@Status IS NULL OR Status = @Status)
        ORDER BY CreatedAtUtc DESC, Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindInvitationById = new(
        "identity.tenant_invitations.find_by_id",
        """
        SELECT Id, TenantId, TargetEmail, TargetUserId, InvitedByUserId,
               MemberRole, TokenHash, Status, ExpiresAtUtc, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_tenant_invitation
        WHERE Id = @InvitationId
          AND TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindInvitationByTokenHash = new(
        "identity.tenant_invitations.find_by_token_hash",
        """
        SELECT Id, TenantId, TargetEmail, TargetUserId, InvitedByUserId,
               MemberRole, TokenHash, Status, ExpiresAtUtc, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_tenant_invitation
        WHERE TokenHash = @TokenHash
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertInvitation = new(
        "identity.tenant_invitations.insert",
        """
        INSERT INTO fn_identity_tenant_invitation
            (Id, TenantId, TargetEmail, TargetUserId, InvitedByUserId, MemberRole,
             TokenHash, Status, ExpiresAtUtc, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @TargetEmail, @TargetUserId, @InvitedByUserId, @MemberRole,
             @TokenHash, @Status, @ExpiresAtUtc, @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement CountActiveMemberSelections = new(
        "identity.tenant_members.count_active_selections",
        """
        SELECT COUNT(1)
        FROM fn_identity_tenant_member AS member
        INNER JOIN fn_identity_user AS userAccount ON userAccount.Id = member.UserId
        WHERE member.TenantId = @TenantId
          AND member.Status = @ActiveStatus
          AND userAccount.IsActive = 1
          AND userAccount.ScopeKey = 'host'
          AND userAccount.TenantId IS NULL
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListActiveMemberSelectionsSqlServer = new(
        "identity.tenant_members.list_active_selections.sql_server",
        """
        SELECT userAccount.Id, userAccount.Username, userAccount.DisplayName, userAccount.PreferredLocale
        FROM fn_identity_tenant_member AS member
        INNER JOIN fn_identity_user AS userAccount ON userAccount.Id = member.UserId
        WHERE member.TenantId = @TenantId
          AND member.Status = @ActiveStatus
          AND userAccount.IsActive = 1
          AND userAccount.ScopeKey = 'host'
          AND userAccount.TenantId IS NULL
        ORDER BY userAccount.NormalizedUsername, userAccount.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListActiveMemberSelectionsMySql = new(
        "identity.tenant_members.list_active_selections.mysql",
        """
        SELECT userAccount.Id, userAccount.Username, userAccount.DisplayName, userAccount.PreferredLocale
        FROM fn_identity_tenant_member AS member
        INNER JOIN fn_identity_user AS userAccount ON userAccount.Id = member.UserId
        WHERE member.TenantId = @TenantId
          AND member.Status = @ActiveStatus
          AND userAccount.IsActive = 1
          AND userAccount.ScopeKey = 'host'
          AND userAccount.TenantId IS NULL
        ORDER BY userAccount.NormalizedUsername, userAccount.Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindActiveMemberSelectionByUserId = new(
        "identity.tenant_members.find_active_selection_by_user_id",
        """
        SELECT userAccount.Id, userAccount.Username, userAccount.DisplayName, userAccount.PreferredLocale
        FROM fn_identity_tenant_member AS member
        INNER JOIN fn_identity_user AS userAccount ON userAccount.Id = member.UserId
        WHERE member.TenantId = @TenantId
          AND member.UserId = @UserId
          AND member.Status = @ActiveStatus
          AND userAccount.IsActive = 1
          AND userAccount.ScopeKey = 'host'
          AND userAccount.TenantId IS NULL
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateInvitationStatus = new(
        "identity.tenant_invitations.update_status",
        """
        UPDATE fn_identity_tenant_invitation
        SET Status = @Status,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @InvitationId
          AND TenantId = @TenantId
          AND Version = @Version
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
}
