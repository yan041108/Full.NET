using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Execution;

/// <summary>每份客户端同意仅能被一个投递预留；它是本地许可，不是厂商保证的库存。</summary>
internal sealed class NotificationSubscriptionGovernor(IQueryExecutor queries, ICommandExecutor commands,
    ICommandTransaction transaction, IOptions<DatabaseOptions> database)
{
    /// <summary>返回 1 为已预留，0 为无授权，-1 为旧调用待对账；所有外部调用在事务外。</summary>
    public Task<int> ReserveAsync(Guid deliveryId, long leaseGeneration, string tenantScopeKey, Guid userId,
        string appId, string templateId, Guid profileVersionId, CancellationToken cancellationToken) => transaction.ExecuteAsync(async token =>
    {
        var parameters = NotificationPlatformSqlParameters.Create(("DeliveryId", deliveryId),
            ("LeaseGeneration", leaseGeneration), ("TenantScopeKey", tenantScopeKey), ("UserId", userId),
            ("AppId", appId), ("TemplateId", templateId), ("ProfileVersionId", profileVersionId));
        var bindingId = await queries.QuerySingleOrDefaultAsync<Guid>(database.Value.Provider == DatabaseProvider.MySql
            ? NotificationSubscriptionGrantSql.LockBindingMySql : NotificationSubscriptionGrantSql.LockBindingSqlServer,
            parameters, token).ConfigureAwait(false);
        if (bindingId == Guid.Empty) return 0;
        await commands.ExecuteAsync(NotificationSubscriptionGrantSql.SuspendAbandoned, parameters, token).ConfigureAwait(false);
        if (await queries.QuerySingleOrDefaultAsync<long>(NotificationSubscriptionGrantSql.CountUnresolved, parameters, token)
            .ConfigureAwait(false) > 0) return -1;
        var grantId = await queries.QuerySingleOrDefaultAsync<Guid>(database.Value.Provider == DatabaseProvider.MySql
            ? NotificationSubscriptionGrantSql.FindAvailableMySql : NotificationSubscriptionGrantSql.FindAvailableSqlServer,
            parameters, token).ConfigureAwait(false);
        if (grantId == Guid.Empty) return 0;
        parameters["Id"] = grantId;
        return await commands.ExecuteAsync(NotificationSubscriptionGrantSql.Reserve, parameters, token).ConfigureAwait(false);
    }, cancellationToken);
}
