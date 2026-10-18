using System.Text.Json.Serialization;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Hosting.Observability;
using ZiggyCreatures.Caching.Fusion.Internals.Distributed;

namespace Full.NET.Modules.Settings.Serialization;

// 只登记真实 L2 载荷的闭合条目，缓存格式不得跟随 HTTP 的 Web 命名策略变化。
[JsonSerializable(typeof(FusionCacheDistributedEntry<GridPreferenceResponse>))]
[JsonSerializable(typeof(FusionCacheDistributedEntry<DiagnosticPolicyDocument>))]
internal partial class SettingsCacheJsonSerializerContext : JsonSerializerContext;
