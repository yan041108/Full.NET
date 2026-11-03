using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.SearchHostDocuments;

/// <summary>来源拥有权限与查询；搜索壳不能跨上下文或指定搜索主体。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapGet("/api/v1/document/host/items/search",
        async (string? q, int? page, HttpContext http, HostDocumentSearchService service, IApiResultMapper mapper, CancellationToken token) =>
        {
            if (http.Request.Query.Any(pair => pair.Key is not ("q" or "page") || pair.Value.Count != 1))
                return mapper.Map(Result<HostDocumentSearchPage>.Failure(HostDocumentSearchService.Invalid()), http);
            return mapper.Map(await service.SearchAsync(q, page ?? 1, token).ConfigureAwait(false), http);
        }).WithName("documentHostSearchItems").WithTags("DocumentHostItems")
        .Produces<HostDocumentSearchPage>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentPermissions.Read));
}

/// <summary>固定最近九十个 UTC 自然日与最多百项，权限先于业务 SQL 校验。</summary>
internal sealed class HostDocumentSearchService(IQueryExecutor queries, ICurrentSessionAuthorization authorization,
    ICurrentTenant tenant, IClock clock, IOptions<DatabaseOptions> database)
{
    internal async Task<Result<HostDocumentSearchPage>> SearchAsync(string? query, int page, CancellationToken token)
    {
        var actor = await authorization.AuthorizeAsync(HostDocumentPermissions.Read, token).ConfigureAwait(false);
        if (actor is null || actor.UserId == Guid.Empty || actor.TenantId is not null || !tenant.IsAvailable || !tenant.IsHost)
            return Result<HostDocumentSearchPage>.Failure(new Error(DocumentErrorCodes.AccessDenied, "Document search access denied.", ErrorType.Forbidden));
        query = query?.Trim();
        if (query is not { Length: >= 2 and <= 64 } || query.Any(char.IsControl) || page is < 1 or > 20)
            return Result<HostDocumentSearchPage>.Failure(Invalid());
        var until = clock.UtcNow.UtcDateTime.Date.AddDays(1);
        var pattern = "%" + query.Replace("~", "~~", StringComparison.Ordinal).Replace("%", "~%", StringComparison.Ordinal)
            .Replace("_", "~_", StringComparison.Ordinal).Replace("[", "~[", StringComparison.Ordinal) + "%";
        var statement = database.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DocumentSearchSql.SqlServer, DatabaseProvider.MySql => DocumentSearchSql.MySql,
            _ => throw new InvalidOperationException("文档搜索不支持当前数据库提供程序。"),
        };
        var rows = await queries.QueryAsync<HostDocumentSearchHit>(statement, DocumentSqlParameters.Create(("ActorUserId", actor.UserId),
            ("SinceUtc", until.AddDays(-90)), ("UntilUtc", until), ("Pattern", pattern), ("Offset", (page - 1) * 5)), token).ConfigureAwait(false);
        return Result<HostDocumentSearchPage>.Success(new(rows.Take(5).ToArray(), page, page < 20 && rows.Count > 5, page == 20 && rows.Count > 5, 90));
    }

    internal static Error Invalid() => new(DocumentErrorCodes.Invalid, "Search requires 2 to 64 characters and page 1 to 20.", ErrorType.Validation);
}
