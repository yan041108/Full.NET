namespace Full.NET.Modules.Jobs.Execution;

/// <summary>按 HandlerKind 解析已注册 <see cref="IJobHandlerExecutor"/> 的注册表。</summary>
internal sealed class JobHandlerKindRegistry(IEnumerable<IJobHandlerExecutor> executors)
{
    private readonly IReadOnlyDictionary<string, IJobHandlerExecutor> _executors =
        executors.ToDictionary(
            executor => executor.HandlerKind,
            StringComparer.Ordinal);

    public bool TryGetExecutor(string handlerKind, out IJobHandlerExecutor? executor) =>
        _executors.TryGetValue(handlerKind, out executor);

    public IReadOnlyList<string> RegisteredHandlerKinds =>
        _executors.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
}
