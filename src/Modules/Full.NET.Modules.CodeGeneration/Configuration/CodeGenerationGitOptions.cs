using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Configuration;

/// <summary>
/// 配置代码生成 Apply 后是否将本地工作区提交同步到 Git 远程；默认关闭，启用时必须提供本地分支、远程名与凭据环境变量。
/// </summary>
internal sealed class CodeGenerationGitOptions
{
    public const string SectionName = "CodeGeneration:Git";

    /// <summary>是否在工作区 Apply 后执行 Git 提交流程；关闭时仅写盘不产生提交。</summary>
    public bool Enabled { get; set; }

    /// <summary>是否在本地提交后推送到远程；启用时 AuthorName 与 AuthorEmail 必填。</summary>
    public bool PushEnabled { get; set; }

    /// <summary>本地提交目标分支名，默认 main。</summary>
    public string DefaultBranch { get; set; } = "main";

    /// <summary>推送目标远程名，默认 origin。</summary>
    public string RemoteName { get; set; } = "origin";

    /// <summary>Git 提交作者名，推送启用时必填。</summary>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>Git 提交作者邮箱，推送启用时必填。</summary>
    public string AuthorEmail { get; set; } = string.Empty;

    /// <summary>存放推送凭据的环境变量名，默认 FULLNET_CODEGENERATION_GIT_TOKEN；禁止把凭据写入配置文件。</summary>
    public string CredentialEnvironmentVariable { get; set; } =
        "FULLNET_CODEGENERATION_GIT_TOKEN";
}

/// <summary>
/// 在启动期校验 Git 选项：启用时必须提供本地分支、远程名与凭据环境变量；推送启用时还要求作者名与邮箱。
/// </summary>
internal sealed class CodeGenerationGitOptionsValidator
    : IValidateOptions<CodeGenerationGitOptions>
{
    /// <summary>
    /// 仅在 Git 启用时校验必填项与推送依赖，避免运行期首次推送才暴露配置缺陷。
    /// </summary>
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