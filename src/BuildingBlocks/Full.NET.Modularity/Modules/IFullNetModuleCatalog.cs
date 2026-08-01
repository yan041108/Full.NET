namespace Full.NET.Modularity.Modules;

/// <summary>只读模块清单快照；由 Composition 在装配时物化，禁止运行时变更。</summary>
public interface IFullNetModuleCatalog
{
    /// <summary>按依赖拓扑顺序返回全部模块描述符。</summary>
    IReadOnlyList<FullNetModuleDescriptor> List();

    /// <summary>按稳定模块键查找描述符。</summary>
    FullNetModuleDescriptor? FindByKey(string moduleKey);
}
