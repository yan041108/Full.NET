using Microsoft.Extensions.Configuration;

namespace Full.NET.Data.Dapper.Outbox;

/// <summary>
/// 将 Native AOT 外部进程测试的显式配置收敛为 Outbox 命令路径。
/// </summary>
internal static class DapperOutboxCommandPathPolicy
{
    internal const string ConfigurationKey = "Testing:OutboxCommandPath";

    /// <summary>
    /// 仅允许 Testing 宿主显式选择候选路径，生产及其他环境始终保持静态 Registry。
    /// </summary>
    internal static DapperOutboxCommandPath Resolve(
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        if (!string.Equals(
                environmentName,
                "Testing",
                StringComparison.OrdinalIgnoreCase))
        {
            return DapperOutboxCommandPath.StaticRegistry;
        }

        var configuredPath = configuration[ConfigurationKey];
        return configuredPath switch
        {
            null or "" or nameof(DapperOutboxCommandPath.StaticRegistry) =>
                DapperOutboxCommandPath.StaticRegistry,
            nameof(DapperOutboxCommandPath.TypedPlan) =>
                DapperOutboxCommandPath.TypedPlan,
            _ => throw new InvalidOperationException(
                $"Configuration '{ConfigurationKey}' must be "
                + $"'{DapperOutboxCommandPath.StaticRegistry}' or "
                + $"'{DapperOutboxCommandPath.TypedPlan}'."),
        };
    }
}
