namespace Full.NET.Modules.Identity.Contracts;

/// <summary>账号操作挑战用途。</summary>
public enum IdentityAccountChallengePurpose : byte
{
    RegistrationEmailVerification = 1,
    PasswordRecovery = 2,
    InvitationEmailVerification = 3,
}

/// <summary>注册策略三态模式。</summary>
public enum IdentityRegistrationMode : byte
{
    Disabled = 0,
    InvitationOnly = 1,
    Open = 2,
}

/// <summary>注册邀请状态。</summary>
public enum IdentityRegistrationInvitationStatus : byte
{
    Pending = 0,
    AccountCreatedPendingOnboarding = 1,
    Activated = 2,
    Revoked = 3,
}

public sealed record AccountChallengeAcceptedResponse(Guid ChallengeId, DateTimeOffset ExpiresAtUtc);

public sealed record VerifyRegistrationInvitationRequest(Guid InvitationId, string InvitationToken);

public sealed record VerifyRegistrationInvitationResponse(
    Guid InvitationId,
    Guid TenantId,
    string Email,
    Guid RegistrationWayId,
    DateTimeOffset ExpiresAtUtc);

public sealed record SendRegistrationEmailChallengeRequest(
    string Email,
    IdentityAccountChallengePurpose Purpose,
    Guid? InvitationId = null,
    string? InvitationToken = null);

public sealed record RegisterAccountRequest(
    string Email,
    string DisplayName,
    string Password,
    Guid ChallengeId,
    string ChallengeCode,
    Guid? RegistrationWayId = null,
    Guid? InvitationId = null,
    string? InvitationToken = null);

public sealed record RegisterAccountResponse(Guid UserId, bool Created);

public sealed record RequestPasswordRecoveryRequest(string Email);

public sealed record ConfirmPasswordRecoveryRequest(
    Guid ChallengeId,
    string ChallengeCode,
    string NewPassword);
