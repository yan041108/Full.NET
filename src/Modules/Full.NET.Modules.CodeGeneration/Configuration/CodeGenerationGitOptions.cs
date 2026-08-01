using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Configuration;

internal sealed class CodeGenerationGitOptions
{
    public const string SectionName = "CodeGeneration:Git";

    public bool Enabled { get; set; }

    public bool PushEnabled { get; set; }

    public string DefaultBranch { get; set; } = "main";

    public string RemoteName { get; set; } = "origin";

    public string AuthorName { get; set; } = string.Empty;

    public string AuthorEmail { get; set; } = string.Empty;

    public string CredentialEnvironmentVariable { get; set; } =
        "FULLNET_CODEGENERATION_GIT_TOKEN";
}

internal sealed class CodeGenerationGitOptionsValidator
    : IValidateOptions<CodeGenerationGitOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        CodeGenerationGitOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.DefaultBranch))
        {
            failures.Add(
                "CodeGeneration:Git:DefaultBranch is required when Git is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.RemoteName))
        {
            failures.Add(
                "CodeGeneration:Git:RemoteName is required when Git is enabled.");
        }

        if (options.PushEnabled)
        {
            if (string.IsNullOrWhiteSpace(options.AuthorName))
            {
                failures.Add(
                    "CodeGeneration:Git:AuthorName is required when push is enabled.");
            }

            if (string.IsNullOrWhiteSpace(options.AuthorEmail))
            {
                failures.Add(
                    "CodeGeneration:Git:AuthorEmail is required when push is enabled.");
            }
        }

        if (string.IsNullOrWhiteSpace(options.CredentialEnvironmentVariable))
        {
            failures.Add(
                "CodeGeneration:Git:CredentialEnvironmentVariable "
                + "must not be empty when Git is enabled.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}