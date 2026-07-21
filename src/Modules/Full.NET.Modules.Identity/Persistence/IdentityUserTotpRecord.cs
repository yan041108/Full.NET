namespace Full.NET.Modules.Identity.Persistence;

/// <summary>Host 账号 TOTP 凭据投影。</summary>
internal sealed class IdentityUserTotpRecord
{
    public IdentityUserTotpRecord()
    {
    }

    public IdentityUserTotpRecord(
        Guid userId,
        string secretProtected,
        bool isEnabled,
        DateTimeOffset? confirmedAtUtc,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? updatedAtUtc,
        int version)
    {
        UserId = userId;
        SecretProtected = secretProtected;
        IsEnabled = isEnabled;
        ConfirmedAtUtc = confirmedAtUtc;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        Version = version;
    }

    public Guid UserId { get; init; }

    public string SecretProtected { get; init; } = string.Empty;

    public bool IsEnabled { get; init; }

    public DateTimeOffset? ConfirmedAtUtc { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? UpdatedAtUtc { get; init; }

    public int Version { get; init; }
}
