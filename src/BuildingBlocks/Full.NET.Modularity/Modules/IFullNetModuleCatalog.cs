namespace Full.NET.Modularity.Modules;

/// <summary>只读模块清单快照；由 Composition 在装配时物化，禁止运行时变更。</summary>
public interface IFullNetModuleCatalog
{
    /// <summary>按依赖拓扑顺序返回全部模块描述符。</summary>
    /// <returns>按依赖拓扑排序的全部官方模块描述符只读列表；无模块时返回空列表，不返回 null。</returns>
    IReadOnlyList<FullNetModuleDescriptor> List();

    /// <summary>按稳定模块键查找描述符。</summary>
    /// <param name="moduleKey">稳定模块键。</param>
    /// <returns>匹配的模块描述符；键不存在时返回 null。</returns>
    FullNetModuleDescriptor? FindByKey(string moduleKey);
}
