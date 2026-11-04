using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageTenantOwnership;

/// <summary>账号停用/退役与 Owner 交接共用用户行锁；先锁账号再检查职责，防止两边均基于旧状态成功。</summary>
internal sealed class TenantOwnerAccountGuard(IQueryExecutor queries, IOptions<DatabaseOptions> options)
{
    internal Task<Guid> LockAsync(Guid userId, CancellationToken cancellationToken) =>
        queries.QuerySingleOrDefaultAsync<Guid>(options.Value.Provider == DatabaseProvider.SqlServer
            ? TenantOwnershipSql.LockHostUserSqlServer : TenantOwnershipSql.LockHostUserMySql,
            Persistence.IdentitySqlParameters.Create(("UserId", userId)), cancellationToken);

    internal async Task<bool> IsProtectedAsync(Guid userId, CancellationToken cancellationToken) =>
        await queries.QuerySingleOrDefaultAsync<int>(TenantOwnershipSql.HasOwnerResponsibility,
            Persistence.IdentitySqlParameters.Create(("UserId", userId)), cancellationToken).ConfigureAwait(false) == 1;
}
