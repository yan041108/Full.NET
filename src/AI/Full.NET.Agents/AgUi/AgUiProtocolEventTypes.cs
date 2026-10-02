namespace Full.NET.Agents.AgUi;

/// <summary>AG-UI 标准事件类型名；SSE event 字段与 JSON type 字段均使用这些常量。</summary>
/// <remarks>常量字符串发布后不可改名或删除；新增常量只能追加到末尾。</remarks>
public static class AgUiProtocolEventTypes
{
    /// <summary>代理运行开始事件；SSE event 与 JSON type 均使用该值，调用方据此初始化流式会话 UI。</summary>
    public const string RunStarted = "RUN_STARTED";

    /// <summary>代理运行正常结束事件；调用方应据此关闭流式输出并释放会话资源。</summary>
    public const string RunFinished = "RUN_FINISHED";

    /// <summary>代理运行异常终止事件；调用方应据此展示错误信息并停止等待后续事件。</summary>
    public const string RunError = "RUN_ERROR";

    /// <summary>代理状态快照事件；调用方据此同步 UI 中间状态，不代表运行结束。</summary>
    public const string StateSnapshot = "STATE_SNAPSHOT";

    /// <summary>自定义扩展事件；调用方应按 payload 内容自行解析，未识别时可忽略。</summary>
    public const string Custom = "CUSTOM";
}
