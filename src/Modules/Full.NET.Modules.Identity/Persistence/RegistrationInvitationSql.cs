using Full.NET.Data.Abstractions;

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
        SELECT InvitationId, TenantId, NormalizedEmail, CredentialHash, RegistrationWayId,
               Status, BoundUserId, ExpiresAtUtc, ConsumedAtUtc, RevokedAtUtc,
               CreatedAtUtc, Version
        FROM fn_identity_registration_invitation
        WHERE InvitationId = @InvitationId
        """,
        SqlDataScope.Global);

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
