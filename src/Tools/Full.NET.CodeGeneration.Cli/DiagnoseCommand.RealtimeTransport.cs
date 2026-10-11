using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    private static void CheckRealtimeTransportConfiguration(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        List<DiagnoseFinding> findings)
    {
        // 无效秘密来源保留专属结果；未声明 Realtime 不扩大既有诊断范围。
        if (findings.Any(finding => finding.Code == "DIAG_USER_SECRETS_INVALID" && finding.Severity == "error"))
            return;
        var paths = ReadRuntimeConfigurationPaths(root, profileSettings, workspacePath, profile);
        if (!paths.Any(path => string.Equals(path, "Realtime", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("Realtime:", StringComparison.OrdinalIgnoreCase)))
            return;
        try
        {
            var configuration = CreateDiagnosticConfiguration(root, profileSettings, workspacePath, profile,
                paths, "Realtime");
            using var configurationLifetime = configuration as IDisposable;
            var options = configuration.GetSection("Realtime").Get<DiagnosticRealtimeTransportOptions>()
                ?? new DiagnosticRealtimeTransportOptions();
            // 宿主先完成全部字段绑定；关闭后才跳过 Hub 路径与传输契约，不能隐藏非法布尔或枚举。
            if (!options.Enabled)
            {
                findings.Add(DiagnoseFinding.Ok("code_generation.realtime.transport.disabled",
                    "Realtime 传输字段已绑定且功能关闭；未验证 Redis、连接亲和部署、授权或宿主启动。"));
                return;
            }
            var hub = options.HubPath;
            var invalidPath = string.IsNullOrWhiteSpace(hub) || !hub.StartsWith('/') || hub.Length == 1
                || hub.EndsWith('/') || hub.Contains("//", StringComparison.Ordinal)
                || hub.Contains('?') || hub.Contains('#') || hub.Any(char.IsWhiteSpace);
            // 与现有宿主保持同一组合约束；不额外拒绝宿主允许绑定的数值枚举。
            var webSocketsWithoutNegotiation = options.TransportMode == DiagnosticRealtimeTransportMode.WebSocketsOnly
                && options.SkipNegotiation;
            if (invalidPath || (options.SkipNegotiation && !webSocketsWithoutNegotiation)
                || (!options.RequireSessionAffinity && !webSocketsWithoutNegotiation))
            {
                AddRealtimeTransportInvalid(findings);
                return;
            }
            findings.Add(DiagnoseFinding.Ok("code_generation.realtime.transport.configured",
                "Realtime 字段绑定及 Hub 路径、传输与亲和组合符合现有 API 注册约束；未验证 Redis、部署、授权或宿主启动。"));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException
            or FormatException or OverflowException)
        {
            AddRealtimeTransportInvalid(findings);
        }
    }

    private static void AddRealtimeTransportInvalid(List<DiagnoseFinding> findings) =>
        // 路径与绑定异常可能含秘密；提示只包含固定字段名，不回显配置或异常。
        findings.Add(DiagnoseFinding.Error("code_generation.realtime.transport.invalid",
            "Realtime 字段不能绑定，或启用后的 Hub 路径、传输与亲和组合不符合现有 API 注册约束。",
            "核对 Enabled、HubPath、TransportMode、SkipNegotiation、RequireSessionAffinity、AllowSharedRedisInDevelopment；跳过协商须使用 WebSocketsOnly，只有该组合可关闭亲和。诊断不输出配置值。"));

    // CLI 保持 Core 运行时闭包，不引用 ASP.NET SignalR；真实注册回归约束默认值、绑定与组合语义。
    private sealed class DiagnosticRealtimeTransportOptions
    {
        public bool Enabled { get; set; } = true;
        public string HubPath { get; set; } = "/hubs/notifications";
        public DiagnosticRealtimeTransportMode TransportMode { get; set; }
        public bool SkipNegotiation { get; set; }
        public bool RequireSessionAffinity { get; set; } = true;
        public bool AllowSharedRedisInDevelopment { get; set; }
    }

    private enum DiagnosticRealtimeTransportMode
    {
        Default = 0,
        WebSocketsOnly = 1,
    }
}
