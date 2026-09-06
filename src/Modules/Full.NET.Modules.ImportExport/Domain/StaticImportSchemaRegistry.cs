using Full.NET.Modules.ImportExport.Contracts;

namespace Full.NET.Modules.ImportExport.Domain;

/// <summary>聚合消费方注册的静态导入 Schema 处理器并提供按键解析。</summary>
internal sealed class StaticImportSchemaRegistry
{
    private readonly IReadOnlyDictionary<string, IStaticImportSchemaHandler> _handlers;

    /// <summary>根据已注册处理器构建只读目录。</summary>
    /// <param name="handlers">消费方通过 DI 注册的 Schema 处理器集合。</param>
    public StaticImportSchemaRegistry(IEnumerable<IStaticImportSchemaHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = handlers
            .GroupBy(handler => handler.SchemaKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.Ordinal);
    }

    /// <summary>返回全部已注册 Schema 元数据，按稳定键排序。</summary>
    public IReadOnlyList<StaticImportSchemaDefinition> ListDefinitions() =>
        _handlers.Values
            .Select(handler => handler.GetDefinition())
            .OrderBy(definition => definition.SchemaKey, StringComparer.Ordinal)
            .ToArray();

    /// <summary>按稳定键解析处理器；不存在时返回 <see langword="null"/>。</summary>
    public IStaticImportSchemaHandler? TryResolve(string schemaKey) =>
        _handlers.TryGetValue(schemaKey, out var handler) ? handler : null;
}
