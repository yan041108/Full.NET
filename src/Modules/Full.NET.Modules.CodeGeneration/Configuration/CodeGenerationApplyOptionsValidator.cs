using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Configuration;

/// <summary>
/// 在宿主启动时拒绝不明确或远程的 Apply 工作区，避免首个写盘请求才暴露配置风险。
/// </summary>
internal sealed class CodeGenerationApplyOptionsValidator
    : IValidateOptions<CodeGenerationApplyOptions>
{
    /// <summary>
    /// 仅在 Apply 启用时校验工作区为已存在的本地绝对目录、非 UNC 远程路径，
    /// 且 MaxRollbackChainLength 落在 2..64 区间，避免首个写盘请求才暴露配置错误。
    /// </summary>
    public ValidateOptionsResult Validate(
        string? name,
        CodeGenerationApplyOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (string.IsNullOrWhiteSpace(options.WorkspaceRoot))
        {
            return Failure();
        }

        try
        {
            if (!Path.IsPathFullyQualified(options.WorkspaceRoot)
                || IsRemotePath(options.WorkspaceRoot))
            {
                return Failure();
            }

            var fullPath = Path.GetFullPath(options.WorkspaceRoot);
            if (!Directory.Exists(fullPath) || File.Exists(fullPath))
            {
                return Failure();
            }
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return Failure();
        }

        if (options.MaxRollbackChainLength is < 2 or > 64)
        {
            return ValidateOptionsResult.Fail(
                "CodeGeneration:Apply:MaxRollbackChainLength must be between 2 and 64 when enabled.");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsRemotePath(string path) =>
        OperatingSystem.IsWindows()
        && path.StartsWith(@"\\", StringComparison.Ordinal);

    private static ValidateOptionsResult Failure() =>
        ValidateOptionsResult.Fail(
            "CodeGeneration:Apply requires an existing absolute local workspace directory when enabled.");
}
