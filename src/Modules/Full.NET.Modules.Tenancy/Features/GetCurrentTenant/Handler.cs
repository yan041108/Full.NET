using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

/// <summary>集成事件或消息处理器；幂等边界由 Inbox/Outbox 保证。</summary>
namespace Full.NET.Modules.Tenancy.Features.GetCurrentTenant;

internal sealed class Handler(IQueryExecutor queryExecutor)
    : IQueryHandler<GetCurrentTenantQuery, TenantSummary>
{
    public async Task<Result<TenantSummary>> HandleAsync(
        GetCurrentTenantQuery query,
        CancellationToken cancellationToken)
    {
        var tenant = await queryExecutor
            .QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                TenantSql.GetCurrent,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return tenant is null
            ? Result<TenantSummary>.Failure(new Error(
                Code: TenancyErrorCodes.NotFound,
                Message: "The current tenant was not found.",
                Type: ErrorType.NotFound))
            : Result<TenantSummary>.Success(tenant.ToSummary());
    }
}
