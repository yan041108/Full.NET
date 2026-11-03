using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageOidcAuthorizations;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.MyOidcAuthorizations;

/// <summary>已认证用户仅管理自己的同意，不接受请求中的 subject 或租户选择。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/me/oidc-authorizations").RequireAuthorization();
        group.MapGet("/", async (HttpContext context, IQueryExecutor queries, IOptions<DatabaseOptions> database,
            ICurrentTenantContextWriter tenant, CancellationToken token) =>
        {
            if (!Guid.TryParse(context.User.FindFirstValue("sub"), out var user)) return Results.Unauthorized();
            return await InHostAsync(tenant, async () =>
            {
                var rows = await queries.QueryAsync<IdentityOidcAuthorizationDetailRow>(database.Value.Provider == DatabaseProvider.MySql
                    ? IdentityOidcSql.ListOwnPermanentMySql : IdentityOidcSql.ListOwnPermanentSqlServer,
                    IdentitySqlParameters.Create(("Subject", user.ToString("D"))), token).ConfigureAwait(false);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(rows.Select(OidcAuthorizationQueryService.Map).ToArray());
            }).ConfigureAwait(false);
        }).WithName("identityListMyOidcAuthorizations").Produces<OidcAuthorizationResponse[]>();
        group.MapPost("/{id:guid}/revoke", async (Guid id, HttpContext context, ICurrentTenantContextWriter tenant,
            OidcAuthorizationManagementService management, IApiResultMapper mapper, CancellationToken token) =>
        {
            if (!Guid.TryParse(context.User.FindFirstValue("sub"), out var user)) return Results.Unauthorized();
            return await InHostAsync<IResult>(tenant, async () =>
            {
                var result = await management.RevokeAsync(id, new(user, context.Connection.RemoteIpAddress?.ToString(), context.Request.Headers.UserAgent.ToString()), token, ownOnly: true).ConfigureAwait(false);
                return result.IsSuccess ? Results.NoContent() : mapper.Map(result, context);
            }).ConfigureAwait(false);
        }).WithName("identityRevokeMyOidcAuthorization").Produces(StatusCodes.Status204NoContent)
            .RequireRateLimiting(IdentityModule.SessionMutationRateLimitPolicy);
    }

    private static async Task<T> InHostAsync<T>(ICurrentTenantContextWriter tenant, Func<Task<T>> action)
    {
        var wasHost = tenant.IsHost; var previous = tenant.Id is Guid id ? new TenantContext(id, tenant.Identifier!, tenant.Name!) : null;
        tenant.SetHost();
        try { return await action().ConfigureAwait(false); }
        finally { if (previous is not null) tenant.SetTenant(previous); else if (wasHost) tenant.SetHost(); else tenant.Clear(); }
    }
}
