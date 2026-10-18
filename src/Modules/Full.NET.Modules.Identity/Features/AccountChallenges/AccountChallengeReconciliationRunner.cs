using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.AccountChallenges;

internal sealed record AccountChallengeReconciliationPage(int Scanned, int Reconciled, Guid? NextAfterId);

/// <summary>按数据库主键顺序扫描一页挑战，仅撤销已完成或到期的未确认投递，不访问 Notifications 或重发凭据。</summary>
/// <remarks>游标按提供程序自身 Guid 排序解释；重启或遍历结束后从头幂等重扫，回补游标之前的状态变化。</remarks>
internal sealed class AccountChallengeReconciliationRunner(
    IQueryExecutor queryExecutor, ICommandExecutor commandExecutor, IClock clock, IOptions<DatabaseOptions> database)
{
    public async Task<AccountChallengeReconciliationPage> RunPageAsync(Guid? afterId, int batchSize, CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(batchSize));
        cancellationToken.ThrowIfCancellationRequested();
        var statement = database.Value.Provider switch
        {
            DatabaseProvider.SqlServer => AccountChallengeSql.ScanDeliverySqlServer,
            DatabaseProvider.MySql => AccountChallengeSql.ScanDeliveryMySql,
            _ => throw new InvalidOperationException("Unsupported challenge reconciliation database provider."),
        };
        // 扫描全部主键页，避免按稀疏状态过滤导致每轮重扫大表；返回量和写入量均受批大小限制。
        var page = await queryExecutor.QueryAsync<AccountChallengeRecord>(statement,
            IdentitySqlParameters.Create(("AfterId", afterId), ("BatchSize", batchSize)), cancellationToken).ConfigureAwait(false);
        if (page.Count > batchSize) throw new InvalidOperationException("Challenge reconciliation exceeded its page boundary.");
        var now = clock.UtcNow;
        var reconciled = 0;
        foreach (var row in page)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.DeliveryStateKey is not ("unknown" or "rejected") || row.DeliveryReconciledAtUtc.HasValue
                || (!row.DeliveryCompletedAtUtc.HasValue && row.ExpiresAtUtc > now)) continue;
            // 读取后可能并发完成受理；原有按标识 CAS 再核对状态，不能用页快照覆盖最新事实。
            var affected = await commandExecutor.ExecuteAsync(AccountChallengeSql.ReconcileDelivery,
                IdentitySqlParameters.Create(("ChallengeId", row.ChallengeId), ("Now", now)), cancellationToken).ConfigureAwait(false);
            if (affected is < 0 or > 1) throw new InvalidOperationException("Challenge reconciliation violated its row boundary.");
            reconciled += affected;
        }
        return new(page.Count, reconciled, page.Count == batchSize ? page[^1].ChallengeId : null);
    }
}
