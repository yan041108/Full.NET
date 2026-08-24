using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Messaging.Kafka.Serialization;

/// <summary>
/// Kafka Connect 管理面 JSON 的源生成闭包；与 Integration Event MemoryPack 载荷分离。
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(KafkaConnectAdminClient.ConnectorRegistration))]
[JsonSerializable(typeof(KafkaConnectAdminClient.ConnectorOffsetsResponse))]
[JsonSerializable(typeof(KafkaConnectAdminClient.ConnectorOffsetEntry))]
[JsonSerializable(typeof(List<KafkaConnectAdminClient.ConnectorOffsetEntry>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class KafkaMessagingJsonSerializerContext : JsonSerializerContext;
