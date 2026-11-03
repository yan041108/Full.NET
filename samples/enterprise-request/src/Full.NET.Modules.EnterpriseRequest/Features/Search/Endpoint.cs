using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.EnterpriseRequest.Features.Search;

/// <summary>应用自有搜索不进入官方框架；业务读取权限在原模块权威校验。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapGet("/api/v1/enterprise_request/enterprise-requests/search",
        async (string? q, int? page, HttpContext http, EnterpriseRequestSearchService service, IApiResultMapper mapper, CancellationToken token) =>
        {
            if (http.Request.Query.Any(pair => pair.Key is not ("q" or "page") || pair.Value.Count != 1))
                return mapper.Map(Result<EnterpriseRequestSearchPage>.Failure(EnterpriseRequestListQuery.Invalid()), http);
            return mapper.Map(await service.SearchAsync(q, page ?? 1, token).ConfigureAwait(false), http);
        }).WithName("enterpriseRequestSearchRequests").WithTags("EnterpriseRequestEnterpriseRequests")
        .Produces<EnterpriseRequestSearchPage>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
        .RequireAuthorization(FullNetPermissionPolicies.For(EnterpriseRequestPermissions.Read));
}

/// <summary>搜索前解析当前权威主体和机构范围；范围只来自静态受信任投影。</summary>
internal sealed class EnterpriseRequestSearchService(IQueryExecutor queries, ICurrentSessionAuthorization authorization,
    ICurrentTenant tenant, IUserDataScopeResolver scopes, IDataScopeSqlFilterBuilder filters, IClock clock, IOptions<DatabaseOptions> database)
{
    internal async Task<Result<EnterpriseRequestSearchPage>> SearchAsync(string? query, int page, CancellationToken token)
    {
        var actor = await authorization.AuthorizeAsync(EnterpriseRequestPermissions.Read, token).ConfigureAwait(false);
        if (actor is null || actor.UserId == Guid.Empty || actor.TenantId is null || !tenant.IsAvailable || tenant.IsHost || tenant.Id != actor.TenantId)
            return Result<EnterpriseRequestSearchPage>.Failure(new Error(CommonErrorCodes.PermissionDenied, "Search access denied.", ErrorType.Forbidden));
        query = query?.Trim();
        if (query is not { Length: >= 2 and <= 64 } || query.Any(char.IsControl) || page is < 1 or > 20)
            return Result<EnterpriseRequestSearchPage>.Failure(EnterpriseRequestListQuery.Invalid());
        var scope = await scopes.ResolveAsync(actor.UserId, false, token).ConfigureAwait(false);
        var filter = filters.BuildOrganizationUnitFilter(scope, "OrganizationUnitId", actor.UserId);
        var statement = database.Value.Provider switch
        {
            DatabaseProvider.SqlServer => EnterpriseRequestSearchSql.SqlServer, DatabaseProvider.MySql => EnterpriseRequestSearchSql.MySql,
            _ => throw new InvalidOperationException("申请搜索不支持当前数据库提供程序。"),
        };
        var until = clock.UtcNow.UtcDateTime.Date.AddDays(1);
        var pattern = "%" + query.Replace("~", "~~", StringComparison.Ordinal).Replace("%", "~%", StringComparison.Ordinal)
            .Replace("_", "~_", StringComparison.Ordinal).Replace("[", "~[", StringComparison.Ordinal) + "%";
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        { ["SinceUtc"] = until.AddDays(-90), ["UntilUtc"] = until, ["Pattern"] = pattern, ["Offset"] = (page - 1) * 5 };
        if (filter is not null)
        {
            if (!statement.Text.Contains(EnterpriseRequestSearchSql.ScopeMarker, StringComparison.Ordinal))
                throw new InvalidOperationException("搜索语句缺少受信任数据范围插入点。");
            statement = statement with { Name = statement.Name + ".data_scope", Text = statement.Text.Replace(EnterpriseRequestSearchSql.ScopeMarker,
                "AND (" + filter.Sql + ")", StringComparison.Ordinal) };
            if (filter.Parameters is IReadOnlyDictionary<string, object?> parameters)
                foreach (var (key, value) in parameters) arguments.Add(key, value);
            else if (filter.Parameters is not null) throw new InvalidOperationException("数据范围参数必须使用静态字典。");
        }
        var rows = await queries.QueryAsync<EnterpriseRequestSearchHit>(statement, arguments, token).ConfigureAwait(false);
        return Result<EnterpriseRequestSearchPage>.Success(new(rows.Take(5).ToArray(), page, page < 20 && rows.Count > 5, page == 20 && rows.Count > 5, 90));
    }
}
