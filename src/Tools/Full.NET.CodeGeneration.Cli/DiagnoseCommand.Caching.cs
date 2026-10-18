using System.Text.Json;
using Full.NET.Caching.Fusion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    private static void CheckCachingConfiguration(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        List<DiagnoseFinding> findings)
    {
        // 无效秘密来源已有专属错误；未声明缓存时保持既有诊断范围。
        if (findings.Any(finding => finding.Code == "DIAG_USER_SECRETS_INVALID" && finding.Severity == "error"))
            return;
        var paths = ReadRuntimeConfigurationPaths(root, profileSettings, workspacePath, profile);
        if (!paths.Any(path => string.Equals(path, "Cache", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("Cache:", StringComparison.OrdinalIgnoreCase)))
            return;
        try
        {
            var configuration = CreateDiagnosticConfiguration(root, profileSettings, workspacePath, profile,
                paths, "Cache", "Realtime", "ConnectionStrings");
            using var configurationLifetime = configuration as IDisposable;
            // 仅执行现有启动注册校验，不构建宿主或解析缓存、Backplane、连接及 HostedService。
            _ = new ServiceCollection().AddFullNetCaching(configuration,
                string.Equals(profile, "production", StringComparison.OrdinalIgnoreCase) ? "Production" : "Development");
            findings.Add(DiagnoseFinding.Ok("code_generation.cache.configuration.configured",
                "缓存配置绑定及现有静态约束已通过；未验证 Redis 可用性、物理隔离或实际缓存行为。"));
        }
        catch (Exception exception) when (exception is OptionsValidationException or InvalidOperationException
            or ArgumentException or FormatException or OverflowException)
        {
            // 缓存条目名、Redis 参数和异常可能包含秘密，仅输出固定配置路径提示。
            findings.Add(DiagnoseFinding.Error("code_generation.cache.configuration.invalid",
                "缓存配置不能绑定或不符合现有静态约束。",
                "核对 Cache 的 DefaultDuration、Jitter、Entries 与 RedisConnectionString，以及 Realtime 的现有共用规则；诊断不解析缓存实例，不打开连接，不输出配置值或异常。"));
        }
    }
}
