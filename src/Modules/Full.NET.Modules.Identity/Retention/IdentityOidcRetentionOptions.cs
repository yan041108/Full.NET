using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Retention;

internal sealed class IdentityOidcRetentionOptions
{
    public const string SectionName = "Identity:Oidc:Retention";

    /// <summary>
    /// 生产默认关闭；部署方确认适用的授权保留制度后才能启用。
    /// </summary>
    public bool Enabled { get; set; }

    public int RetentionDays { get; set; } = 30;

    public int PollSeconds { get; set; } = 3600;
}

internal sealed class IdentityOidcRetentionOptionsValidator
    : IValidateOptions<IdentityOidcRetentionOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        IdentityOidcRetentionOptions options)
    {
        var failures = new List<string>();
        ValidateRange(
            options.RetentionDays,
            1,
            3650,
            "Identity:Oidc:Retention:RetentionDays",
            failures);
        ValidateRange(
            options.PollSeconds,
            60,
            86400,
            "Identity:Oidc:Retention:PollSeconds",
            failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRange(
        int value,
        int minimum,
        int maximum,
        string key,
        ICollection<string> failures)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add(
                $"{key} must be between {minimum} and {maximum}.");
        }
    }
}