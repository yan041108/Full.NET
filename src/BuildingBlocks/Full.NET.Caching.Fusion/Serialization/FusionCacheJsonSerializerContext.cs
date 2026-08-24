using System.Text.Json.Serialization;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Observability;
using Full.NET.Modules.Settings.Contracts;

namespace Full.NET.Caching.Fusion.Serialization;

/// <summary>
/// FusionCache L2 分布式缓存的 AOT 友好 JSON 源生成上下文；仅登记当前 API 闭包会写入 Redis 的载荷类型。
/// </summary>
[JsonSerializable(typeof(DiagnosticPolicyDocument))]
[JsonSerializable(typeof(DiagnosticPolicyRule))]
[JsonSerializable(typeof(DiagnosticPolicyRule[]))]
[JsonSerializable(typeof(DiagnosticPolicyScopeKind))]
[JsonSerializable(typeof(LoggingPressureState))]
[JsonSerializable(typeof(GridColumnPreference))]
[JsonSerializable(typeof(GridColumnPreference[]))]
[JsonSerializable(typeof(IReadOnlyList<GridColumnPreference>))]
[JsonSerializable(typeof(GridPreferenceResponse))]
[JsonSerializable(typeof(TenantCachePayload))]
[JsonSerializable(typeof(TenantResolutionCacheEntry))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class FusionCacheJsonSerializerContext : JsonSerializerContext;
