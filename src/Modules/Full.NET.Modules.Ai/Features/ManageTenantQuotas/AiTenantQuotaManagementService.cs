using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Features.ManageTenantQuotas;

/// <summary>AI 租户配额创建与更新。</summary>
internal sealed class AiTenantQuotaManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    AiTenantQuotaQueryService queries,
    IIdentityActiveTenantDirectory activeTenants,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>更新或创建租户配额。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的配额或稳定业务错误。</returns>
    public Task<Result<AiTenantQuotaResponse>> UpsertAsync(
        Guid tenantId,
        UpdateAiTenantQuotaRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpsertCoreAsync(tenantId, request, token),
            cancellationToken);

    private async Task<Result<AiTenantQuotaResponse>> UpsertCoreAsync(
        Guid tenantId,
        UpdateAiTenantQuotaRequest request,
        CancellationToken cancellationToken)
    {
        var validationMessage = AiModelConfigFieldValidator.ValidateQuotaLimits(
            request.MonthlyTokenLimit,
            request.MonthlyRequestLimit);
        if (validationMessage is not null)
        {
            return Result<AiTenantQuotaResponse>.Failure(new Error(
                AiErrorCodes.TenantQuotaInvalid,
                validationMessage,
                ErrorType.Validation));
        }

        var tenantExists = await activeTenants.IsActiveTenantAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (!tenantExists)
        {
            return Result<AiTenantQuotaResponse>.Failure(new Error(
                AiErrorCodes.TenantNotFound,
                "The specified tenant does not exist or is not active.",
                ErrorType.Validation));
        }

        var current = await queryExecutor.QuerySingleOrDefaultAsync<AiTenantQuotaRecord>(
                AiTenantQuotaSql.FindByTenantId,
                AiSqlParameters.Create(("TenantId", tenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        var now = clock.UtcNow;
        var monthKey = now.ToString("yyyy-MM");

        if (current is null)
        {
            var quotaId = idGenerator.NewId();
            await commandExecutor.ExecuteAsync(
                    AiTenantQuotaSql.Insert,
                    AiSqlParameters.Create(
                        ("Id", quotaId),
                        ("TenantId", tenantId),
                        ("MonthlyTokenLimit", request.MonthlyTokenLimit),
                        ("MonthlyRequestLimit", request.MonthlyRequestLimit),
                        ("UsedTokensThisMonth", 0L),
                        ("UsedRequestsThisMonth", 0L),
                        ("QuotaMonthKey", monthKey),
                        ("IsEnabled", request.IsEnabled),
                        ("CreatedAtUtc", now),
                        ("UpdatedAtUtc", null),
                        ("Version", 1)),
                    cancellationToken)
                .ConfigureAwait(false);
            return await queries.GetByTenantIdAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
        }

        var affected = await commandExecutor.ExecuteAsync(
                AiTenantQuotaSql.Update,
                AiSqlParameters.Create(
                    ("TenantId", tenantId),
                    ("MonthlyTokenLimit", request.MonthlyTokenLimit),
                    ("MonthlyRequestLimit", request.MonthlyRequestLimit),
                    ("IsEnabled", request.IsEnabled),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return Result<AiTenantQuotaResponse>.Failure(new Error(
                AiErrorCodes.TenantQuotaConcurrencyConflict,
                "The AI tenant quota was modified by another request.",
                ErrorType.Conflict));
        }

        return await queries.GetByTenantIdAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
    }
}
