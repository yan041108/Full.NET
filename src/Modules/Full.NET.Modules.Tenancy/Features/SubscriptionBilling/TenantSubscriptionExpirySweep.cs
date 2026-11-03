using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Tenancy.Features.SubscriptionBilling;

/// <summary>多实例按租户根锁收敛过期状态；功能/配额权威判断本身已校验期限，不依赖本批次及时运行。</summary>
internal sealed class TenantSubscriptionExpirySweep(IQueryExecutor queries, ICommandTransaction transaction,
    TenantSubscriptionWriteBoundary boundary, IClock clock, IOptions<DatabaseOptions> database)
{
    internal async Task SweepAsync(CancellationToken token)
    {
        var ids = await queries.QueryAsync<Guid>(database.Value.Provider == DatabaseProvider.MySql
                ? SubscriptionInvoiceSql.ExpiredTenantsMySql : SubscriptionInvoiceSql.ExpiredTenantsSqlServer,
            TenancySqlParameters.Create(("Now", clock.UtcNow)), token).ConfigureAwait(false);
        foreach (var id in ids)
            await transaction.ExecuteAsync(local => boundary.EnterAsync(id, local, requireActive: false), token).ConfigureAwait(false);
    }
}
