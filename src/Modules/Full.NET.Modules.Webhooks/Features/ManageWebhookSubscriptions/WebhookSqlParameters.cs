// Webhook 订阅 SQL 参数构建；统一租户过滤与分页游标绑定。
namespace Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions;

/// <summary>Webhook 订阅 SQL 参数构建；统一租户过滤与分页游标绑定。</summary>
internal static class WebhookSqlParameters
{
    public static Dictionary<string, object?> Create(params (string Key, object? Value)[] values)
    {
        var parameters = new Dictionary<string, object?>(values.Length, StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            parameters[key] = value;
        }

        return parameters;
    }
}
