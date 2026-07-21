namespace Full.NET.Modularity.Modules;

public sealed class FullNetModuleRegistry
{
    private readonly Dictionary<string, IFullNetModule> _modules =
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

        if (!_modules.TryAdd(moduleKey, module))
        {
            var registeredModule = _modules[moduleKey];
            throw new InvalidOperationException(
                $"Module key '{moduleKey}' is already registered by "
                + $"'{registeredModule.GetType().FullName}' and cannot also identify "
                + $"'{module.GetType().FullName}'.");
        }
    }

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

        if (!_modules.TryGetValue(moduleKey, out var module))
        {
            throw new InvalidOperationException(
                $"Module dependency key '{moduleKey}' is not registered.");
        }

        foreach (var dependencyKey in module.Dependencies
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
        ordered.Add(module);
    }
}
