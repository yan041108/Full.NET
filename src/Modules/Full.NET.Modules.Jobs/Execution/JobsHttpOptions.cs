using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Execution;

/// <summary>HTTP 任务出站网络安全选项；Production 强制禁止私网访问。</summary>
public sealed class JobsHttpOptions
{
    public const string SectionName = "Jobs:Http";

    /// <summary>非 Production 下允许访问私网/环回；Production 必须为 false。</summary>
    public bool AllowPrivateNetwork { get; set; }
}

internal sealed class JobsHttpOptionsValidator(IHostEnvironment hostEnvironment)
    : IValidateOptions<JobsHttpOptions>
{
    public ValidateOptionsResult Validate(string? name, JobsHttpOptions options)
    {
        if (hostEnvironment.IsProduction() && options.AllowPrivateNetwork)
        {
            return ValidateOptionsResult.Fail(
                "Jobs:Http:AllowPrivateNetwork must remain false in Production.");
        }

        return ValidateOptionsResult.Success;
    }
}
