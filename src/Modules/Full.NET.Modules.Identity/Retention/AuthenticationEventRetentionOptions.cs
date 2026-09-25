using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Retention;

/// <summary>认证事件独立保留策略；默认关闭，部署方批准保留期后启用。</summary>
internal sealed class AuthenticationEventRetentionOptions
{
    public const string SectionName = "Identity:AuthenticationEvents:Retention";

    public bool Enabled { get; set; }
    public int RetentionDays { get; set; } = 365;
    public int BatchSize { get; set; } = 200;
    public int MaxBatchesPerRun { get; set; } = 15;
    public int PollSeconds { get; set; } = 3600;
}

internal sealed class AuthenticationEventRetentionOptionsValidator
    : IValidateOptions<AuthenticationEventRetentionOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthenticationEventRetentionOptions options)
    {
        var errors = new List<string>();
        Check(options.RetentionDays, 1, 3650, "RetentionDays", errors);
        Check(options.BatchSize, 1, 2000, "BatchSize", errors);
        Check(options.MaxBatchesPerRun, 1, 100, "MaxBatchesPerRun", errors);
        Check(options.PollSeconds, 60, 86400, "PollSeconds", errors);
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void Check(int value, int minimum, int maximum, string name, ICollection<string> errors)
    {
        if (value < minimum || value > maximum)
        {
            errors.Add($"{AuthenticationEventRetentionOptions.SectionName}:{name} must be between {minimum} and {maximum}.");
        }
    }
}
