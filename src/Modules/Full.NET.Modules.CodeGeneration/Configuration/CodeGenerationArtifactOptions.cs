using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Configuration;

/// <summary>下载包独立于源码与回滚证据保存，目录由部署者显式提供且所有实例必须共享。</summary>
internal sealed class CodeGenerationArtifactOptions
{
    public const string SectionName = "CodeGeneration:Artifacts";
    public string RootDirectory { get; set; } = string.Empty;
    public int RetentionDays { get; set; } = 30;
    public long MaximumBytes { get; set; } = 512L * 1024 * 1024;
    public int MaximumPackages { get; set; } = 4096;
}

/// <summary>拒绝将下载包放入工作区或公开目录；空配置保留只读能力，受跟踪预览返回明确存储不可用。</summary>
internal sealed class CodeGenerationArtifactOptionsValidator(IOptions<CodeGenerationApplyOptions> apply)
    : IValidateOptions<CodeGenerationArtifactOptions>
{
    public ValidateOptionsResult Validate(string? name, CodeGenerationArtifactOptions options)
    {
        if (options.RetentionDays is < 1 or > 365 || options.MaximumBytes is < 16 * 1024 * 1024 or > 16L * 1024 * 1024 * 1024
            || options.MaximumPackages is < 1 or > 16384) return ValidateOptionsResult.Fail("代码生成归档保留或容量配置无效。");
        if (string.IsNullOrEmpty(options.RootDirectory)) return ValidateOptionsResult.Success;
        if (!Path.IsPathFullyQualified(options.RootDirectory) || !Directory.Exists(options.RootDirectory))
            return ValidateOptionsResult.Fail("代码生成归档必须配置已存在的绝对目录。");
        var root = Path.GetFullPath(options.RootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (root.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("wwwroot", StringComparer.OrdinalIgnoreCase))
            return ValidateOptionsResult.Fail("代码生成归档不能放入公开静态目录。");
        if (!string.IsNullOrWhiteSpace(apply.Value.WorkspaceRoot))
        {
            var workspace = Path.GetFullPath(apply.Value.WorkspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (root.Equals(workspace, StringComparison.OrdinalIgnoreCase)
                || root.StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || workspace.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return ValidateOptionsResult.Fail("代码生成归档与 Apply 工作区必须使用互不包含的独立目录。");
        }
        return ValidateOptionsResult.Success;
    }
}
