using System.Text.Json.Serialization;
using Full.NET.Realtime;
using Full.NET.Realtime.SignalR.Features.RealtimeProbe;

namespace Full.NET.Realtime.SignalR.Serialization;

/// <summary>
/// SignalR JSON 协议与 Testing 探针响应的源生成闭包；与 HTTP Hosting 上下文分离。
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RealtimeMessage))]
[JsonSerializable(typeof(RealtimeProbeResponse))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, object?>))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(string))]
internal partial class RealtimeJsonSerializerContext : JsonSerializerContext;
