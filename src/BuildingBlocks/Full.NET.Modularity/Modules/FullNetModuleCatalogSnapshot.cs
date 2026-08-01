namespace Full.NET.Modularity.Modules;

/// <summary>不可变模块清单实现，供 Host API 与架构测试读取。</summary>
public sealed class FullNetModuleCatalogSnapshot : IFullNetModuleCatalog
{
    private readonly IReadOnlyList<FullNetModuleDescriptor> _modules;
    private readonly IReadOnlyDictionary<string, FullNetModuleDescriptor> _byKey;

    private FullNetModuleCatalogSnapshot(IReadOnlyList<FullNetModuleDescriptor> modules)
    {
        _modules = modules;
        _byKey = modules.ToDictionary(
            module => module.ModuleKey,
            StringComparer.Ordinal);
    }

    /// <summary>空清单，供未物化完整 Composition 的测试/Migrator 装配兜底。</summary>
    public static FullNetModuleCatalogSnapshot Empty { get; } = new([]);

    /// <summary>
    /// 从已校验的注册表构建快照；调用前必须能成功完成拓扑排序。
    /// </summary>
    public static FullNetModuleCatalogSnapshot FromRegistry(
        FullNetModuleRegistry registry,
        Func<IFullNetModule, FullNetModuleDescriptor> descriptorFactory)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(descriptorFactory);

        var modules = registry.GetOrderedModules();
        var descriptors = new List<FullNetModuleDescriptor>(modules.Count);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            var descriptor = descriptorFactory(module)
                ?? throw new InvalidOperationException(
                    $"Descriptor factory returned null for module '{module.Name}'.");
            if (!string.Equals(descriptor.ModuleKey, module.Name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Descriptor key '{descriptor.ModuleKey}' does not match module name "
                    + $"'{module.Name}'.");
            }

            if (!seenKeys.Add(descriptor.ModuleKey))
            {
                throw new InvalidOperationException(
                    $"Duplicate module descriptor key '{descriptor.ModuleKey}'.");
            }

            descriptors.Add(descriptor);
        }

        return new FullNetModuleCatalogSnapshot(descriptors);
    }

    /// <inheritdoc />
    public IReadOnlyList<FullNetModuleDescriptor> List() => _modules;

    /// <inheritdoc />
    public FullNetModuleDescriptor? FindByKey(string moduleKey)
    {
        if (string.IsNullOrWhiteSpace(moduleKey))
        {
            return null;
        }

        return _byKey.TryGetValue(moduleKey.Trim(), out var descriptor)
            ? descriptor
            : null;
    }
}
