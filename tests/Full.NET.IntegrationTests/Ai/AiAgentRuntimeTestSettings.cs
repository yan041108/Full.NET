namespace Full.NET.IntegrationTests.Ai;

/// <summary>Integration test Agent Runtime settings.</summary>
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
