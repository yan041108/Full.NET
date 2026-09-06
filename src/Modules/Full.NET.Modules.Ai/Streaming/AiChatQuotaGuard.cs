using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Streaming;

/// <summary>租户 AI 配额门禁：请求前校验、完成后记账。</summary>
internal sealed class AiChatQuotaGuard(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock)
{
    /// <summary>校验租户是否仍可发起一次聊天请求。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>允许或稳定业务错误。</returns>
    public async Task<Result<bool>> EnsureCanStartRequestAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var quota = await queryExecutor.QuerySingleOrDefaultAsync<AiTenantQuotaRecord>(
                AiTenantQuotaSql.FindByTenantId,
                AiSqlParameters.Create(("TenantId", tenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (quota is null || !quota.IsEnabled)
        {
            return Result<bool>.Success(true);
        }

        var monthKey = clock.UtcNow.ToString("yyyy-MM");
        if (!string.Equals(quota.QuotaMonthKey, monthKey, StringComparison.Ordinal))
        {
            await commandExecutor.ExecuteAsync(
                    AiTenantQuotaSql.ResetMonthlyUsage,
                    AiSqlParameters.Create(
                        ("TenantId", tenantId),
                        ("QuotaMonthKey", monthKey),
                        ("UpdatedAtUtc", clock.UtcNow)),
                    cancellationToken)
                .ConfigureAwait(false);
            quota = await queryExecutor.QuerySingleOrDefaultAsync<AiTenantQuotaRecord>(
                    AiTenantQuotaSql.FindByTenantId,
                    AiSqlParameters.Create(("TenantId", tenantId)),
                    cancellationToken)
                .ConfigureAwait(false)
                ?? quota;
        }

        if (quota.MonthlyRequestLimit is { } requestLimit
            && quota.UsedRequestsThisMonth >= requestLimit)
        {
            return Result<bool>.Failure(new Error(
                AiErrorCodes.TenantQuotaExceeded,
                "Monthly AI request quota has been exceeded for this tenant.",
                ErrorType.Forbidden));
        }

        return Result<bool>.Success(true);
    }

    /// <summary>记录一次完成的聊天用量。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="promptTokens">提示 Token 数；未知时按内容估算。</param>
    /// <param name="completionTokens">补全 Token 数；未知时按内容估算。</param>
    /// <param name="promptCharacters">提示字符数，用于估算。</param>
    /// <param name="completionCharacters">补全字符数，用于估算。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RecordUsageAsync(
        Guid tenantId,
        int? promptTokens,
        int? completionTokens,
        int promptCharacters,
        int completionCharacters,
        CancellationToken cancellationToken = default)
    {
        var tokenDelta = (promptTokens ?? EstimateTokens(promptCharacters))
            + (completionTokens ?? EstimateTokens(completionCharacters));
        tokenDelta = Math.Max(1, tokenDelta);

        await commandExecutor.ExecuteAsync(
                AiTenantQuotaSql.IncrementUsage,
                AiSqlParameters.Create(
                    ("TenantId", tenantId),
                    ("TokenDelta", (long)tokenDelta),
                    ("RequestDelta", 1L),
                    ("QuotaMonthKey", clock.UtcNow.ToString("yyyy-MM")),
                    ("UpdatedAtUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static int EstimateTokens(int characters) =>
        Math.Max(1, characters / 4);
}
