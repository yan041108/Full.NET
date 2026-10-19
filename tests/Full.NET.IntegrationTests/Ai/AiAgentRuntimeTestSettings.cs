namespace Full.NET.IntegrationTests.Ai;

/// <summary>集成测试用 Agent 运行时配置节；拉长轮询避免与生产 Worker 争用。</summary>
internal static class AiAgentRuntimeTestSettings
{
    internal static Dictionary<string, string?> ForIntegrationTests() => new()
    {
        ["FullNet:Ai:AgentRuntime:AcceptsNewRuns"] = "true",
        ["FullNet:Ai:AgentRuntime:RuntimeVersion"] = "1",
        ["FullNet:Ai:AgentRuntime:PollMilliseconds"] = "60000",
        ["FullNet:Ai:AgentRuntime:MaxRunDurationSeconds"] = "3600",
    };
}
