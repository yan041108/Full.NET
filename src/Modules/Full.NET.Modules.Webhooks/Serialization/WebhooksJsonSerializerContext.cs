// Webhooks 模块 JSON 源生成上下文；Native AOT 绑定契约类型，避免反射序列化。
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Modules.Webhooks.Contracts;

namespace Full.NET.Modules.Webhooks.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(WebhookSubscriptionResponse))]
[JsonSerializable(typeof(Full.NET.Modules.Webhooks.Features.DeliverWebhooks.WebhookDeliveryInsertRow[]))]
[JsonSerializable(typeof(WebhookSubscriptionResponse[]))]
[JsonSerializable(typeof(IReadOnlyList<WebhookSubscriptionResponse>))]
[JsonSerializable(typeof(CreateWebhookSubscriptionRequest))]
[JsonSerializable(typeof(WebhookDeliveryResponse))]
/// <summary>Webhooks 模块 JSON 源生成上下文；Native AOT 绑定已审计契约，禁止反射序列化。</summary>
internal partial class WebhooksJsonSerializerContext : JsonSerializerContext;

/// <summary>投递载荷保留既有 PascalCase 线格式，避免改变签名原文和消费端字段。</summary>
[JsonSerializable(typeof(WebhookEventEnvelope))]
/// <summary>Webhooks 模块 JSON 源生成上下文；Native AOT 绑定已审计契约，禁止反射序列化。</summary>
internal partial class WebhookPayloadJsonSerializerContext : JsonSerializerContext;
