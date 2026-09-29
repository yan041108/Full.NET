using Full.NET.Hosting.Observability;

namespace Full.NET.Modules.Settings.Features.ManageHostConfigEntries;

/// <summary>专用控制面拥有的配置键，通用配置项 API 不可读写。</summary>
internal static class ReservedHostConfigKeys
{
    public static bool IsReserved(string? configKey) =>
        string.Equals(
            configKey,
            DiagnosticPolicyLimits.ConfigKey,
            StringComparison.OrdinalIgnoreCase);
}
