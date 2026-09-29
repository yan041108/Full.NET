namespace Full.NET.Hosting.Observability;

/// <summary>
/// 中间件生成的 B2 摘要值快照。仅供快照器读取，不把 ILogger 属性当作来源证明。
/// </summary>
internal sealed class HttpOperationLogRecord
{
    private readonly KeyValuePair<string, object?>[] _fields;

    internal HttpOperationLogRecord(IEnumerable<KeyValuePair<string, object?>> fields)
    {
        _fields = fields
            .Select(field => new KeyValuePair<string, object?>(field.Key, field.Value))
            .ToArray();
        IsPriority = _fields.Any(field =>
            field.Key == "reliability.class"
            && field.Value is "Priority");
    }

    internal IReadOnlyList<KeyValuePair<string, object?>> Fields => _fields;

    internal bool IsPriority { get; }

    public override string ToString() => "[http operation record]";
}
