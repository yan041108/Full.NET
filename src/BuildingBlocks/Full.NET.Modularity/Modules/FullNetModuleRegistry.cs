namespace Full.NET.Modularity.Modules;

public sealed class FullNetModuleRegistry
{
    private readonly Dictionary<Type, IFullNetModule> _modules = [];

    public void Add(IFullNetModule module)
    {
        var moduleType = module.GetType();
        if (!_modules.TryAdd(moduleType, module))
        {
            throw new InvalidOperationException(
                $"Module '{moduleType.FullName}' is already registered.");
        }
    }

    public IReadOnlyList<IFullNetModule> GetOrderedModules()
    {
        var ordered = new List<IFullNetModule>(_modules.Count);
        var permanent = new HashSet<Type>();
        var temporary = new HashSet<Type>();

        foreach (var moduleType in _modules.Keys)
        {
            Visit(moduleType, permanent, temporary, ordered);
        }

        return ordered;
    }

    private void Visit(
        Type moduleType,
        ISet<Type> permanent,
        ISet<Type> temporary,
        ICollection<IFullNetModule> ordered)
    {
        if (permanent.Contains(moduleType))
        {
            return;
        }

        if (!temporary.Add(moduleType))
        {
            throw new InvalidOperationException(
                $"A module dependency cycle contains '{moduleType.FullName}'.");
        }

        if (!_modules.TryGetValue(moduleType, out var module))
        {
            throw new InvalidOperationException(
                $"Module dependency '{moduleType.FullName}' is not registered.");
        }

        foreach (var dependency in module.Dependencies)
        {
            Visit(dependency, permanent, temporary, ordered);
        }

        temporary.Remove(moduleType);
        permanent.Add(moduleType);
        ordered.Add(module);
    }
}
