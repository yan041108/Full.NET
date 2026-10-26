using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>从随程序集嵌入的共享目录读取分支边界，避免设计器接受无法发布的图。</summary>
internal static class WorkflowGatewayLimits
{
    private static readonly JsonElement Limits = Load();

    /// <summary>读取不含默认出口的条件分支上限；并行网关按全部分支计数。</summary>
    /// <param name="nodeTypeKey">闭合网关类型键。</param>
    public static int MaximumBranches(string nodeTypeKey) =>
        Limits.GetProperty(nodeTypeKey).GetProperty("maximumBranches").GetInt32();

    /// <summary>读取条件分支下限；默认出口始终单独校验。</summary>
    /// <param name="nodeTypeKey">闭合网关类型键。</param>
    public static int MinimumBranches(string nodeTypeKey) =>
        Limits.GetProperty(nodeTypeKey).GetProperty("minimumBranches").GetInt32();

    public static bool HasDefaultBranch(string nodeTypeKey) =>
        Limits.GetProperty(nodeTypeKey).GetProperty("hasDefaultBranch").GetBoolean();

    private static JsonElement Load()
    {
        using var stream = typeof(WorkflowGatewayLimits).Assembly
            .GetManifestResourceStream("Full.NET.Workflow.GatewayLimits.json")
            ?? throw new InvalidOperationException("workflow.gateway_limits_missing");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
