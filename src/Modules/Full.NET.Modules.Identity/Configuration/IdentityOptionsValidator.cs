using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Configuration;

internal sealed class IdentityOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<IdentityOptions>
{
    public ValidateOptionsResult Validate(string? name, IdentityOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add("Identity issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Identity audience is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("Identity client id is required.");
        }

        if (options.AccessTokenMinutes is < 1 or > 60)
        {
            failures.Add("AccessTokenMinutes must be between 1 and 60.");
        }

        if (options.RefreshTokenDays is < 1 or > 90)
        {
            failures.Add("RefreshTokenDays must be between 1 and 90.");
        }

        if (options.LockoutThreshold is < 1 or > 20)
        {
            failures.Add("LockoutThreshold must be between 1 and 20.");
        }

        if (options.LockoutMinutes is < 1 or > 1440)
        {
            failures.Add("LockoutMinutes must be between 1 and 1440.");
        }

        if (options.LoginRateLimitPermitLimitPerMinute < 1)
        {
            failures.Add(
                "LoginRateLimitPermitLimitPerMinute must be at least 1.");
        }

        if (options.SessionMutationRateLimitPermitLimitPerMinute < 1)
        {
            failures.Add(
                "SessionMutationRateLimitPermitLimitPerMinute must be at least 1.");
        }

        if (options.SigningKeys is null)
        {
            failures.Add("Identity SigningKeys configuration is required.");
        }
        else if (options.SigningKeys.Values.Any(value => value is null))
        {
            failures.Add(
                "Identity SigningKeys configuration must not contain null entries.");
        }

        if (options.AllowedOrigins is null)
        {
            failures.Add("Identity AllowedOrigins configuration is required.");
        }

        if (options.Bootstrap is null)
        {
            failures.Add("Identity Bootstrap configuration is required.");
        }

        var supportsEphemeralSigning = environment.IsDevelopment()
            || environment.IsEnvironment("Testing");
        if (options.AllowDevelopmentEphemeralSigningKey && !supportsEphemeralSigning)
        {
            failures.Add(
                "Ephemeral signing keys are allowed only in Development or Testing.");
        }

        if (options.EnableTokenEndpoints
            && !(options.AllowDevelopmentEphemeralSigningKey && supportsEphemeralSigning)
            && !HasConfiguredActiveSigningKey(options))
        {
            failures.Add(
                "A production signing key and matching ActiveKeyId are required.");
        }

        if (options.EnableRemoteSuperAdministratorManagement
            && environment.IsProduction()
            && !options.EnableTotpStrongReauthentication)
        {
            failures.Add(
                "Remote super-administrator management cannot be enabled in Production until a strong reauthentication provider is configured.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool HasConfiguredActiveSigningKey(IdentityOptions options) =>
        !string.IsNullOrWhiteSpace(options.ActiveKeyId)
        && options.SigningKeys is not null
        && options.SigningKeys.TryGetValue(options.ActiveKeyId, out var key)
        && key is not null
        && !string.IsNullOrWhiteSpace(key.PublicKeyPem)
        && !string.IsNullOrWhiteSpace(key.PrivateKeyPem);
}
