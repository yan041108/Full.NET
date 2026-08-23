using System.Text.Json.Serialization;

namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// Messaging 抽象层非 HTTP JSON 的源生成闭包；CDC 位点序列化必须保持历史 JSON 形状兼容。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CdcDeliveryPosition))]
[JsonSerializable(typeof(MySqlBinlogCoordinates))]
[JsonSerializable(typeof(SqlServerCdcLsnCoordinates))]
internal partial class MessagingJsonSerializerContext : JsonSerializerContext;
