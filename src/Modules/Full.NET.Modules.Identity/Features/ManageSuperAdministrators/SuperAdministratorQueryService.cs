using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageSuperAdministrators;

/// <summary>集中封装超级管理员列表与审计的双数据库只读查询。</summary>
internal sealed class SuperAdministratorQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public Task<IReadOnlyList<SuperAdministratorResponse>> ListAsync(
        CancellationToken cancellationToken = default) =>
        queryExecutor.QueryAsync<SuperAdministratorResponse>(
            IdentitySql.ListSuperAdministrators,
            cancellationToken: cancellationToken);

    public Task<IReadOnlyList<SuperAdministratorAuditResponse>> ListAuditsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer =>
                IdentitySql.ListSuperAdministratorAuditsSqlServer,
            DatabaseProvider.MySql =>
                IdentitySql.ListSuperAdministratorAuditsMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        return queryExecutor.QueryAsync<SuperAdministratorAuditResponse>(
            statement,
            new { Limit = Math.Clamp(limit, 1, 200) },
            cancellationToken);
    }
}
