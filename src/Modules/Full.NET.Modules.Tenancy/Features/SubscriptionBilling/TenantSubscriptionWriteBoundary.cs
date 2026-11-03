using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Tenancy.Features.SubscriptionBilling;

/// <summary>同租户的人工订阅和付费订阅共用根锁；调用方必须已进入 Tenancy 本地命令事务。</summary>
internal sealed class TenantSubscriptionWriteBoundary(IQueryExecutor queries, ICommandExecutor commands,
    IOptions<DatabaseOptions> database, IClock clock)
{
    internal async Task<bool> EnterAsync(Guid tenantId, CancellationToken token, bool requireActive = true)
    {
        var id = await queries.QuerySingleOrDefaultAsync<Guid>(database.Value.Provider == DatabaseProvider.MySql
                ? SubscriptionInvoiceSql.LockTenantMySql : SubscriptionInvoiceSql.LockTenantSqlServer,
            TenancySqlParameters.Create(("TenantId", tenantId), ("RequireActive", requireActive)), token).ConfigureAwait(false);
        if (id == Guid.Empty) return false;
        await commands.ExecuteAsync(SubscriptionInvoiceSql.ExpireForTenant,
            TenancySqlParameters.Create(("TenantId", tenantId), ("Now", clock.UtcNow)), token).ConfigureAwait(false);
        return true;
    }
}
