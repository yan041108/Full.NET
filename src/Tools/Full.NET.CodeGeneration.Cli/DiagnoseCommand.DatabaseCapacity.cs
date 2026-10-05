using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    private static void CheckDatabaseCapacity(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        List<DiagnoseFinding> findings)
    {
        // 无效或缺少数据库前置配置已有专属结果，不能误报为预算失败。
        if (!findings.Any(finding => finding.Code == "DIAG_CONNECTION_CONFIGURED")
            || findings.Any(finding => finding.Severity == "error"
                && (finding.Code.StartsWith("DIAG_DATABASE_", StringComparison.Ordinal)
                    || finding.Code == "DIAG_USER_SECRETS_INVALID")))
            return;

        // 未声明预算时维持既有诊断范围，避免以预算名义扩展全部 Database Options 检查。
        var paths = ReadRuntimeConfigurationPaths(root, profileSettings, workspacePath, profile);
        if (!paths.Any(path => string.Equals(path, "DatabaseCapacity", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("DatabaseCapacity:", StringComparison.OrdinalIgnoreCase)))
            return;

        try
        {
            // 复用诊断已验证的逐叶合并；特殊连接环境前缀须先映射为命名连接路径。
            var effective = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawPath in paths)
            {
                var path = rawPath;
                foreach (var prefix in ConnectionEnvironmentPrefixes)
                    if (path.StartsWith(prefix.Prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        path = "ConnectionStrings:" + path[prefix.Prefix.Length..];
                        break;
                    }
                if (!new[] { "Database", "DatabaseCapacity", "ConnectionStrings" }.Any(section =>
                    string.Equals(path, section, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(section + ":", StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (TryReadIdentitySigningValue(root, profileSettings, workspacePath, profile, path, out var value))
                    effective.TryAdd(path, value);
            }
            var builder = new ConfigurationBuilder().AddInMemoryCollection(effective);
            var configuration = builder.Build();
            using var configurationLifetime = configuration as IDisposable;

            // 只解析现有 Options 校验；不解析连接工厂、会话或宿主服务，不打开数据库连接。
            using var runtime = new ServiceCollection()
                .AddFullNetDapper(configuration,
                    string.Equals(profile, "production", StringComparison.OrdinalIgnoreCase) ? "Production" : "Development")
                .BuildServiceProvider();
            var capacity = runtime.GetRequiredService<IOptions<DatabaseCapacityOptions>>().Value;
            findings.Add(capacity.Enabled
                ? DiagnoseFinding.Ok("code_generation.database_capacity.configured",
                    "数据库连接预算绑定与静态约束已通过；未证明实际吞吐、连接可用性或部署容量。")
                : DiagnoseFinding.Ok("code_generation.database_capacity.disabled",
                    "数据库连接预算未启用；配置绑定已通过，未验证数据库容量。"));
        }
        catch (Exception exception) when (exception is OptionsValidationException or InvalidOperationException
            or ArgumentException or FormatException or OverflowException)
        {
            // Options 或驱动异常可能含配置值；仅返回固定路径提示，不输出异常或预算数值。
            findings.Add(DiagnoseFinding.Error("code_generation.database_capacity.invalid",
                "数据库连接预算配置不能绑定或不符合现有静态约束。",
                "核对 DatabaseCapacity 的开关、宿主角色和数值；启用时连接池须开启，ExpectedMaxPoolSize 须匹配实际连接串及角色池上限，许可证与保留量、各角色副本连接总量不能超预算。诊断不输出配置值，不打开连接。"));
        }
    }
}
