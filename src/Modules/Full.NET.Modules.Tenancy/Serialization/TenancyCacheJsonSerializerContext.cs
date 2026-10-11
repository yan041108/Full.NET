using System.Text.Json.Serialization;
using Full.NET.Modules.Tenancy.Persistence;
using ZiggyCreatures.Caching.Fusion.Internals.Distributed;

namespace Full.NET.Modules.Tenancy.Serialization;

// L2 使用 FusionCache 默认格式；独立于 HTTP 的 Web 命名策略，保持 JIT/AOT 互读。
[JsonSerializable(typeof(FusionCacheDistributedEntry<TenantResolutionCacheEntry>))]
internal partial class TenancyCacheJsonSerializerContext : JsonSerializerContext;
