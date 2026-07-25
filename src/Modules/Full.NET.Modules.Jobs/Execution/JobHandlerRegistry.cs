namespace Full.NET.Modules.Jobs.Execution;

/// <summary>按 JobKey 解析已注册处理器。</summary>
internal sealed class JobHandlerRegistry(IEnumerable<IJobHandler> handlers)
{
    private readonly IReadOnlyDictionary<string, IJobHandler> _handlers =
        handlers.ToDictionary(handler => handler.JobKey, StringComparer.Ordinal);

    public bool TryGetHandler(string jobKey, out IJobHandler? handler) =>
        _handlers.TryGetValue(jobKey, out handler);
}
