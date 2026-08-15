namespace Full.NET.Modularity.Modules;

public sealed class FullNetModuleRegistry
{
    private readonly Dictionary<string, ModuleRegistration> _modules =
        new(StringComparer.Ordinal);

    public void Add(IFullNetModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var moduleKey = module.Name;
        if (string.IsNullOrWhiteSpace(moduleKey))
        {
            throw new InvalidOperationException(
                $"Module key '{moduleKey}' must not be blank.");
        }

        var dependencyKeys = module.Dependencies?.ToArray()
            ?? throw new InvalidOperationException(
                $"Module key '{moduleKey}' must declare a dependency collection.");
        var declaredDependencyKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < dependencyKeys.Length; index++)
        {
            var dependencyKey = dependencyKeys[index];
            if (string.IsNullOrWhiteSpace(dependencyKey))
            {
                throw new InvalidOperationException(
                    $"Module key '{moduleKey}' declares a null or blank dependency key "
                    + $"at index {index}.");
            }

            if (!declaredDependencyKeys.Add(dependencyKey))
            {
                throw new InvalidOperationException(
                    $"Module key '{moduleKey}' declares duplicate dependency key "
                    + $"'{dependencyKey}'.");
            }
        }

        var registration = new ModuleRegistration(moduleKey, dependencyKeys, module);
        if (!_modules.TryAdd(moduleKey, registration))
        {
            var registeredModule = _modules[moduleKey];
            throw new InvalidOperationException(
                $"Module key '{moduleKey}' is already registered by "
                + $"'{registeredModule.Module.GetType().FullName}' and cannot also identify "
                + $"'{module.GetType().FullName}'.");
        }
    }

    /// <summary>
    /// 基于 DFS 拓扑排序返回按依赖顺序排列的模块列表；先执行依赖的模块先出列。
    /// </summary>
    /// <remarks>
    /// 首次调用时执行完整校验：缺失依赖、循环依赖、注册后名称变更均会抛异常终止装配。
    /// 相同入度下按稳定键 Ordinal 排序，保证跨进程启动顺序一致。
    /// </remarks>
    public IReadOnlyList<IFullNetModule> GetOrderedModules()
    {
        var ordered = new List<IFullNetModule>(_modules.Count);
        var permanent = new HashSet<string>(StringComparer.Ordinal);
        var temporary = new HashSet<string>(StringComparer.Ordinal);

        foreach (var moduleKey in _modules.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            Visit(moduleKey, permanent, temporary, ordered);
        }

        return ordered;
    }

    private void Visit(
        string moduleKey,
        ISet<string> permanent,
        ISet<string> temporary,
        ICollection<IFullNetModule> ordered)
    {
        if (permanent.Contains(moduleKey))
        {
            return;
        }

        if (!temporary.Add(moduleKey))
        {
            throw new InvalidOperationException(
                $"A module dependency cycle contains module key '{moduleKey}'.");
        }

        if (!_modules.TryGetValue(moduleKey, out var registration))
        {
            throw new InvalidOperationException(
                $"Module dependency key '{moduleKey}' is not registered.");
        }

        var currentModuleKey = registration.Module.Name;
        if (!string.Equals(registration.Key, currentModuleKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Module registered with stable key '{registration.Key}' now reports "
                + $"mutable key '{currentModuleKey ?? "<null>"}'. Module keys must not change "
                + "after registration.");
        }

        foreach (var dependencyKey in registration.Dependencies
                     .OrderBy(key => key, StringComparer.Ordinal))
        {
            if (!_modules.ContainsKey(dependencyKey))
            {
                throw new InvalidOperationException(
                    $"Module key '{moduleKey}' depends on unknown module key "
                    + $"'{dependencyKey}'.");
            }

            Visit(dependencyKey, permanent, temporary, ordered);
        }

        temporary.Remove(moduleKey);
        permanent.Add(moduleKey);
        ordered.Add(registration.Module);
    }

    private sealed record ModuleRegistration(
        string Key,
        IReadOnlyList<string> Dependencies,
        IFullNetModule Module);
}
