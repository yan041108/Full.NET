using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Hosting.Security;

/// <summary>
/// 多实例共享 Data Protection Key Ring 配置。证书材料只通过路径引用，不把 PFX 密码写入仓库。
/// </summary>
public sealed class DataProtectionOptions
{
    public const string SectionName = "DataProtection";

    /// <summary>稳定应用名；API/Worker 必须一致。</summary>
    public string ApplicationName { get; set; } = "Full.NET";

    /// <summary>共享 Key Ring 目录的绝对路径；Production 禁止临时目录。</summary>
    public string? KeyRingPath { get; set; }

    /// <summary>活动加密证书（PFX/PEM）路径。</summary>
    public string? CertificatePath { get; set; }

    /// <summary>活动证书密码；应来自环境变量或 Secret，不得提交到仓库。</summary>
    public string? CertificatePassword { get; set; }

    /// <summary>历史解密证书路径；轮换后仍需解开旧密钥。</summary>
    public string[] HistoricalCertificatePaths { get; set; } = [];

    /// <summary>与 HistoricalCertificatePaths 一一对应的可选密码；缺省复用 CertificatePassword。</summary>
    public string?[] HistoricalCertificatePasswords { get; set; } = [];
}

internal sealed class DataProtectionOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<DataProtectionOptions>
{
    public ValidateOptionsResult Validate(string? name, DataProtectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ApplicationName))
        {
            failures.Add($"{DataProtectionOptions.SectionName}:ApplicationName is required.");
        }

        var isProduction = environment.IsProduction();
        if (isProduction)
        {
            if (string.IsNullOrWhiteSpace(options.KeyRingPath))
            {
                failures.Add(
                    $"{DataProtectionOptions.SectionName}:KeyRingPath is required in Production.");
            }
            else if (!Path.IsPathRooted(options.KeyRingPath))
            {
                failures.Add(
                    $"{DataProtectionOptions.SectionName}:KeyRingPath must be an absolute path.");
            }
            else if (DataProtectionPathRules.IsTemporaryDirectory(options.KeyRingPath))
            {
                failures.Add(
                    $"{DataProtectionOptions.SectionName}:KeyRingPath must not use a temporary directory.");
            }

            if (string.IsNullOrWhiteSpace(options.CertificatePath))
            {
                failures.Add(
                    $"{DataProtectionOptions.SectionName}:CertificatePath is required in Production.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(options.KeyRingPath)
            && !Path.IsPathRooted(options.KeyRingPath)
            && !options.KeyRingPath.StartsWith("~", StringComparison.Ordinal))
        {
            // 非绝对路径在非 Production 允许相对 ContentRoot，由扩展方法解析。
        }

        if (options.HistoricalCertificatePaths.Length
            != options.HistoricalCertificatePasswords.Length
            && options.HistoricalCertificatePasswords.Length > 0)
        {
            failures.Add(
                $"{DataProtectionOptions.SectionName}:HistoricalCertificatePasswords length must match paths or be empty.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

internal static class DataProtectionPathRules
{
    public static bool IsTemporaryDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        var temp = Path.GetFullPath(Path.GetTempPath());
        if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 额外拒绝常见临时段，避免把 Key Ring 放到可被 OS 清理的位置。
        return full.Contains($"{Path.DirectorySeparatorChar}Temp{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || full.Contains($"{Path.AltDirectorySeparatorChar}Temp{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || full.Contains("/tmp/", StringComparison.OrdinalIgnoreCase);
    }
}
