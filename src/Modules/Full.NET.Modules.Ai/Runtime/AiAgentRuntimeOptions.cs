namespace Full.NET.Modules.Ai.Runtime;

/// <summary>持久 Agent 运行时门禁；默认关闭新 Run 直至 Worker 心跳与版本匹配。</summary>
public sealed class AiAgentRuntimeOptions
{
    public const string SectionName = "FullNet:Ai:AgentRuntime";

    /// <summary>是否允许 API 接受新运行；仍要求新鲜 Worker 心跳。</summary>
    public bool AcceptsNewRuns { get; set; }

    /// <summary>Worker 与 API 共同识别的运行时版本。</summary>
    public string RuntimeVersion { get; set; } = "1";

    /// <summary>单次运行最长持续时间。</summary>
    public int MaxRunDurationSeconds { get; set; } = 300;

    /// <summary>租约持续时间。</summary>
    public int LeaseSeconds { get; set; } = 30;

    /// <summary>Worker 轮询间隔。</summary>
    public int PollMilliseconds { get; set; } = 1000;

    /// <summary>每轮最多领取运行数。</summary>
    public int BatchSize { get; set; } = 4;
}
