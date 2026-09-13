namespace Full.NET.Agents.AgUi;

/// <summary>AG-UI 标准事件类型名；SSE event 字段与 JSON type 字段均使用这些常量。</summary>
public static class AgUiProtocolEventTypes
{
    public const string RunStarted = "RUN_STARTED";
    public const string RunFinished = "RUN_FINISHED";
    public const string RunError = "RUN_ERROR";
    public const string StateSnapshot = "STATE_SNAPSHOT";
    public const string Custom = "CUSTOM";
}
