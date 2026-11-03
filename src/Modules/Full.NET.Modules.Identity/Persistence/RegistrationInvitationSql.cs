using Full.NET.Data.Abstractions;

/// <summary>Identity 模块显式 SQL 语句；参数化访问，作用域在声明处标注。</summary>
namespace Full.NET.Modules.Identity.Persistence;

internal static class RegistrationInvitationSql
{
    public static readonly SqlStatement Insert = new(
        "identity.insert_registration_invitation",
        """
        INSERT INTO fn_identity_registration_invitation
            (InvitationId, TenantId, NormalizedEmail, CredentialHash, RegistrationWayId,
             Status, BoundUserId, ExpiresAtUtc, ConsumedAtUtc, RevokedAtUtc,
             CreatedAtUtc, Version)
        VALUES
            (@InvitationId, @TenantId, @NormalizedEmail, @CredentialHash, @RegistrationWayId,
             @Status, NULL, @ExpiresAtUtc, NULL, NULL, @CreatedAtUtc, 1)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindById = new(
        "identity.find_registration_invitation_by_id",
        """
        SELECT registration.InvitationId, registration.TenantId, registration.NormalizedEmail,
               registration.CredentialHash, registration.RegistrationWayId, registration.Status,
               registration.BoundUserId, registration.ExpiresAtUtc, registration.ConsumedAtUtc,
               registration.RevokedAtUtc, registration.CreatedAtUtc, registration.Version
        FROM fn_identity_registration_invitation AS registration
        INNER JOIN fn_identity_tenant_invitation AS invitation
          ON invitation.Id = registration.InvitationId AND invitation.TenantId = registration.TenantId
        WHERE registration.InvitationId = @InvitationId
          AND invitation.Status IN ('Pending', 'Accepted')
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement BindTenantInvitee = new(
        "identity.registration_invitation.bind_tenant_invitee",
        """
        UPDATE fn_identity_tenant_invitation
        SET TargetUserId = @UserId, UpdatedAtUtc = @Now, Version = Version + 1
        WHERE Id = @InvitationId AND TenantId = @TenantId AND TargetUserId IS NULL
          AND LOWER(TargetEmail) = @NormalizedEmail AND Status = 'Pending' AND ExpiresAtUtc > @Now
        """, SqlDataScope.Global);

    public static readonly SqlStatement RevokeRegistration = new(
        "identity.registration_invitation.revoke",
        """
        UPDATE fn_identity_registration_invitation
        SET Status = 3, RevokedAtUtc = @Now, Version = Version + 1
        WHERE InvitationId = @InvitationId AND TenantId = @TenantId AND Status IN (0, 1)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement MarkActivated = new(
        "identity.registration_invitation.activate",
        """
        UPDATE fn_identity_registration_invitation
        SET Status = 2, BoundUserId = @UserId, Version = Version + 1
        WHERE InvitationId = @InvitationId AND TenantId = @TenantId
          AND ((BoundUserId = @UserId AND Status = 1) OR (BoundUserId IS NULL AND Status = 0))
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement MarkConsumed = new(
        "identity.consume_registration_invitation_credential",
        """
        UPDATE fn_identity_registration_invitation
        SET ConsumedAtUtc = @ConsumedAtUtc,
            Version = Version + 1
        WHERE InvitationId = @InvitationId
          AND ConsumedAtUtc IS NULL
          AND RevokedAtUtc IS NULL
          AND ExpiresAtUtc > @ConsumedAtUtc
          AND CredentialHash = @CredentialHash
          AND Status = @ExpectedStatus
          AND Version = @Version
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement BindUser = new(
        "identity.bind_registration_invitation_user",
        """
        UPDATE fn_identity_registration_invitation
        SET BoundUserId = @BoundUserId,
            Status = @Status,
            Version = Version + 1
        WHERE InvitationId = @InvitationId
          AND RevokedAtUtc IS NULL
          AND Version = @Version
        """,
        SqlDataScope.Global);
}
