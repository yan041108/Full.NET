using Full.NET.Hosting.Api;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Settings.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Settings.Features.ManageQuerySchemes;

/// <summary>用户方案只从当前可信会话取主体；请求不能指定所有者或租户。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/me/query-schemes/{gridKey}").WithTags("SettingsQuerySchemes").RequireAuthorization();
        group.MapGet("/schema", async (string gridKey, QuerySchemeService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            mapper.Map(await service.GetSchemaAsync(gridKey, token).ConfigureAwait(false), http))
            .WithName("settingsGetQuerySchemeSchema").Produces<QuerySchemeDefinition>().ProducesProblem(403).ProducesProblem(404)
            .RequireAuthorization(FullNetPermissionPolicies.For(QuerySchemePermissions.Read));
        group.MapGet("/", async (string gridKey, bool? shared, int? pageSize, string? cursor,
            QuerySchemeService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            mapper.Map(await service.ListAsync(gridKey, shared ?? false, pageSize ?? 20, cursor, token).ConfigureAwait(false), http))
            .WithName("settingsListQuerySchemes").Produces<CursorPage<QuerySchemeSummary>>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(404)
            .RequireAuthorization(FullNetPermissionPolicies.For(QuerySchemePermissions.Read));
        group.MapGet("/{schemeId:guid}", async (string gridKey, Guid schemeId, QuerySchemeService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            mapper.Map(await service.GetAsync(gridKey, schemeId, token).ConfigureAwait(false), http))
            .WithName("settingsGetQueryScheme").Produces<QuerySchemeDetail>().ProducesProblem(403).ProducesProblem(404)
            .RequireAuthorization(FullNetPermissionPolicies.For(QuerySchemePermissions.Read));
        group.MapPost("/", async (string gridKey, SaveQuerySchemeRequest request, QuerySchemeService service,
            IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
        {
            var result = await service.SaveAsync(gridKey, null, request, token).ConfigureAwait(false);
            return result.IsSuccess ? Results.Created($"/api/v1/me/query-schemes/{gridKey}/{result.Value!.Summary.Id:D}", result.Value) : mapper.Map(result, http);
        }).WithName("settingsCreateQueryScheme").Produces<QuerySchemeDetail>(201).ProducesProblem(400).ProducesProblem(403).ProducesProblem(404)
            .WithMetadata(new RequestSizeLimitAttribute(20 * 1024))
            .RequireAuthorization(FullNetPermissionPolicies.For(QuerySchemePermissions.Create));
        group.MapPut("/{schemeId:guid}", async (string gridKey, Guid schemeId, SaveQuerySchemeRequest request,
            QuerySchemeService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            mapper.Map(await service.SaveAsync(gridKey, schemeId, request, token).ConfigureAwait(false), http))
            .WithName("settingsUpdateQueryScheme").Produces<QuerySchemeDetail>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .WithMetadata(new RequestSizeLimitAttribute(20 * 1024))
            .RequireAuthorization(FullNetPermissionPolicies.For(QuerySchemePermissions.Update));
        group.MapDelete("/{schemeId:guid}", async (string gridKey, Guid schemeId, [FromBody] DeleteQuerySchemeRequest request,
            QuerySchemeService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            mapper.Map(await service.DeleteAsync(gridKey, schemeId, request.Version, token).ConfigureAwait(false), http))
            .WithName("settingsDeleteQueryScheme").Produces<bool>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(QuerySchemePermissions.Delete));
        group.MapPut("/{schemeId:guid}/sharing", async (string gridKey, Guid schemeId, ShareQuerySchemeRequest request,
            QuerySchemeService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            mapper.Map(await service.ShareAsync(gridKey, schemeId, request, token).ConfigureAwait(false), http))
            .WithName("settingsShareQueryScheme").Produces<QuerySchemeDetail>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(QuerySchemePermissions.Share));
        group.MapGet("/default", async (string gridKey, QuerySchemeService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            mapper.Map(await service.GetDefaultAsync(gridKey, token).ConfigureAwait(false), http))
            .WithName("settingsGetDefaultQueryScheme").Produces<DefaultQuerySchemeResponse>().ProducesProblem(403).ProducesProblem(404)
            .RequireAuthorization(FullNetPermissionPolicies.For(QuerySchemePermissions.Read));
        group.MapPut("/default", async (string gridKey, SetDefaultQuerySchemeRequest request,
            QuerySchemeService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            mapper.Map(await service.SetDefaultAsync(gridKey, request, token).ConfigureAwait(false), http))
            .WithName("settingsSetDefaultQueryScheme").Produces<DefaultQuerySchemeResponse>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(QuerySchemePermissions.SetDefault));
    }
}
