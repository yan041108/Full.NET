using System.Text.Json.Serialization;
using Full.NET.Hosting.Serialization;

namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>Host 控制面的当前席位及存储账面指标；读取结果不替代业务准入判断。</summary>
/// <param name="TenantId">权威查询得到的目标企业。</param>
/// <param name="TenantIsActive">本次读取时的企业活动状态。</param>
/// <param name="ReadAtUtc">本次读取完成的时间，不是业务授权有效期。</param>
/// <param name="Metrics">非周期正式指标，最多席位、存储两项；未配置时为空。</param>
public sealed record TenantQuotaUsageSummaryResponse(Guid TenantId, bool TenantIsActive, DateTimeOffset ReadAtUtc,
    IReadOnlyList<TenantQuotaUsageItemResponse> Metrics);

/// <summary>当前账面用量；Int64 使用精确十进制字符串，避免客户端大整数精度丢失。</summary>
/// <param name="Id">原指标身份。</param>
/// <param name="MetricCode">正式指标编码。</param>
/// <param name="PeriodKey">固定的非周期维度。</param>
/// <param name="LimitValue">账面上限。</param>
/// <param name="UsedValue">已确认占用。</param>
/// <param name="ReservedValue">尚未确认的预留。</param>
public sealed record TenantQuotaUsageItemResponse(Guid Id, string MetricCode, string PeriodKey,
    [property: JsonConverter(typeof(StringInt64JsonConverter))] long LimitValue,
    [property: JsonConverter(typeof(StringInt64JsonConverter))] long UsedValue,
    [property: JsonConverter(typeof(StringInt64JsonConverter))] long ReservedValue);

/// <summary>单企业用量的只读时点观察；未知或进行中不提供差额判断。</summary>
/// <param name="TenantId">精确目标企业。</param>
/// <param name="MetricCode">席位或资源存储指标。</param>
/// <param name="TenantIsActive">最终账面观察时的企业状态。</param>
/// <param name="MetricId">当前指标身份，未配置时为空。</param>
/// <param name="BookUsedValue">账面已确认量，精确整数文本；未配置时未知。</param>
/// <param name="ReservedValue">账面预留，精确整数文本；未配置时未知。</param>
/// <param name="AuthoritativeUsedValue">所有者计数端口的观察值，不可用时未知。</param>
/// <param name="ObservedDifference">权威值减账面值的精确整数文本；仅可比较时提供。</param>
/// <param name="State">Aligned、Different、InProgress、Unconfigured 或 Unavailable。</param>
/// <param name="ReadAtUtc">本次观察完成时间，不是准入或修复资格。</param>
public sealed record TenantQuotaUsageDiagnosticResponse(Guid TenantId, string MetricCode, bool TenantIsActive,
    Guid? MetricId, string? BookUsedValue, string? ReservedValue, string? AuthoritativeUsedValue,
    string? ObservedDifference, string State, DateTimeOffset ReadAtUtc);
