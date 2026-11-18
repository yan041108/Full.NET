using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Modules.Settings.Contracts;
using ZiggyCreatures.Caching.Fusion.Internals.Distributed;

namespace Full.NET.Modules.Settings.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.General)]
[JsonSerializable(typeof(FusionCacheDistributedEntry<GridPreferenceResponse>))]
// 诊断策略由权威轮询维护有时效的进程快照，不纳入通用 L2 缓存载荷。
/// <summary>Settings 拥有的实际 SDK L2 包装闭包；与 JIT 默认格式一致，独立于 HTTP Web 命名。</summary>
internal partial class SettingsCacheJsonSerializerContext : JsonSerializerContext;
