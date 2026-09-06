using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Features.ManageTenantQuotas;

/// <summary>AI 租户配额响应映射。</summary>
internal static class AiTenantQuotaMapper
{
    /// <summary>映射列表项。</summary>
    /// <param name="row">持久化行。</param>
    /// <returns>列表响应。</returns>
    public static AiTenantQuotaListItem MapListItem(AiTenantQuotaRecord row) =>
        new(
            row.Id,
            row.TenantId,
            row.MonthlyTokenLimit,
            row.MonthlyRequestLimit,
            row.UsedTokensThisMonth,
            row.UsedRequestsThisMonth,
            row.QuotaMonthKey,
            row.IsEnabled,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    /// <summary>映射详情。</summary>
    /// <param name="row">持久化行。</param>
    /// <returns>详情响应。</returns>
    public static AiTenantQuotaResponse MapDetail(AiTenantQuotaRecord row) =>
        new(
            row.Id,
            row.TenantId,
            row.MonthlyTokenLimit,
            row.MonthlyRequestLimit,
            row.UsedTokensThisMonth,
            row.UsedRequestsThisMonth,
            row.QuotaMonthKey,
            row.IsEnabled,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);
}
