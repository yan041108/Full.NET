using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Mqtt.Contracts;

namespace Full.NET.Modules.Mqtt.Serialization;

/// <summary>MQTT 模块 JSON 源生成上下文。</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(MqttBrokerStatusResponse))]
[JsonSerializable(typeof(MqttClientResponse))]
[JsonSerializable(typeof(MqttMessageResponse))]
[JsonSerializable(typeof(PublishMqttMessageRequest))]
[JsonSerializable(typeof(IReadOnlyList<MqttClientResponse>))]
[JsonSerializable(typeof(PagedResult<MqttMessageResponse>))]
/// <summary>Mqtt 模块 JSON 源生成上下文；Native AOT 绑定已审计契约，禁止反射序列化。</summary>
internal partial class MqttJsonSerializerContext : JsonSerializerContext;
