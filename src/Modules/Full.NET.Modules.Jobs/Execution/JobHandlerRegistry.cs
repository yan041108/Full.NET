namespace Full.NET.Modules.Jobs.Execution;

/// <summary>
/// 按 JobKey 解析已注册 IJobHandler 处理器的注册表（IJobHandlerFactory 简化形式）。
/// 从 DI 容器收集所有以 IEnumerable{IJobHandler} 注册的处理器实例，构建 JobKey→IJobHandler 只读字典，
/// TryGetHandler 用于 HostJobDefinitionManagementService 创建/更新时校验 JobKey 已注册，
/// 以及 JobExecutionRunner 执行时定位具体处理器。
/// </summary>
internal sealed class JobHandlerRegistry(IEnumerable<IJobHandler> handlers)
{
    private readonly IReadOnlyDictionary<string, IJobHandler> _handlers =
        handlers.ToDictionary(handler => handler.JobKey, StringComparer.Ordinal);

    public bool TryGetHandler(string jobKey, out IJobHandler? handler) =>
        _handlers.TryGetValue(jobKey, out handler);
}
