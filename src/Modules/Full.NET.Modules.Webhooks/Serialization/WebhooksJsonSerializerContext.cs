using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Modules.Webhooks.Contracts;

namespace Full.NET.Modules.Webhooks.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(WebhookSubscriptionResponse))]
[JsonSerializable(typeof(WebhookSubscriptionResponse[]))]
[JsonSerializable(typeof(IReadOnlyList<WebhookSubscriptionResponse>))]
[JsonSerializable(typeof(CreateWebhookSubscriptionRequest))]
[JsonSerializable(typeof(WebhookDeliveryResponse))]
internal partial class WebhooksJsonSerializerContext : JsonSerializerContext;

/// <summary>投递载荷保留既有 PascalCase 线格式，避免改变签名原文和消费端字段。</summary>
[JsonSerializable(typeof(WebhookEventEnvelope))]
internal partial class WebhookPayloadJsonSerializerContext : JsonSerializerContext;
