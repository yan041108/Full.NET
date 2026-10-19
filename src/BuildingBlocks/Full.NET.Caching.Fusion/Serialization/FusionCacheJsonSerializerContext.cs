using System.Text.Json;
using System.Text.Json.Serialization;
using ZiggyCreatures.Caching.Fusion.Internals.Distributed;

namespace Full.NET.Caching.Fusion.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.General)]
[JsonSerializable(typeof(FusionCacheDistributedEntry<long>))]
/// <summary>基础设施拥有 SDK 标签/清空时间戳包装；保持默认 Redis 格式，不贡献业务类型。</summary>
internal partial class FusionCacheJsonSerializerContext : JsonSerializerContext;
