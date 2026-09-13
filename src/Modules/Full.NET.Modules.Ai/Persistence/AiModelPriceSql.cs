using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>通过同模块模型范围确认价格可见性；配置更换 Provider/Model 后旧价格不再适用。</summary>
internal static class AiModelPriceSql
{
    private const string Query = """
        SELECT price.Id, price.Currency, price.InputPerMillion, price.OutputPerMillion, price.CachedInputPerMillion
        FROM fn_ai_model_price AS price JOIN fn_ai_model_config AS model ON model.Id = price.ModelConfigId
        WHERE price.ModelConfigId = @ModelConfigId AND price.ModelId = @ModelId AND price.ProviderKey = @ProviderKey AND price.ModelId = model.ModelId AND price.ProviderKey = model.ProviderKey
          AND (model.TenantId IS NULL OR model.TenantId = @ScopeTenantId) AND model.IsEnabled = 1 AND price.ValidFromUtc <= @Now
        ORDER BY price.ValidFromUtc DESC, price.Id DESC
        """;
    public static readonly SqlStatement FindSqlServer = new("ai.find_model_price.sqlserver", Query + " OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY", SqlDataScope.Global);
    public static readonly SqlStatement FindMySql = new("ai.find_model_price.mysql", Query + " LIMIT 1", SqlDataScope.Global);
}
