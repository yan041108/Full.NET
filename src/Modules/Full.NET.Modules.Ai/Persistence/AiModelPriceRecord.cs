namespace Full.NET.Modules.Ai.Persistence;

/// <summary>价格按版本追加，由受控 Host 运维维护，不从模型回答或请求参数采信。</summary>
internal sealed class AiModelPriceRecord
{
    public Guid Id { get; init; }
    public string Currency { get; init; } = string.Empty;
    public decimal InputPerMillion { get; init; }
    public decimal OutputPerMillion { get; init; }
    public decimal CachedInputPerMillion { get; init; }
}
