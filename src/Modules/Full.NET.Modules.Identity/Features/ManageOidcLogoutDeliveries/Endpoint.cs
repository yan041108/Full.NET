using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageOidcLogoutDeliveries;

internal sealed record IdentityOidcLogoutDeliveryResponse(Guid Id, string ClientId, string StatusKey, int AttemptCount,
    DateTimeOffset NextAttemptAtUtc, string? LastErrorCode, DateTimeOffset CreatedAtUtc, DateTimeOffset? DeliveredAtUtc);

/// <summary>以现有客户端精确权限查询有界投递状态与审计重试，不提供修改目的地或投递载荷的入口。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/oidc-logout-deliveries").WithTags("IdentityOidcClients");
        group.MapGet("/", async (IQueryExecutor queries, IOptions<DatabaseOptions> database, CancellationToken token) =>
        {
            var rows = await queries.QueryAsync<IdentityOidcLogoutDeliveryRow>(database.Value.Provider == DatabaseProvider.MySql
                ? IdentityOidcLogoutSql.ListMySql : IdentityOidcLogoutSql.ListSqlServer, cancellationToken: token).ConfigureAwait(false);
            return Results.Ok(rows.Select(row => new IdentityOidcLogoutDeliveryResponse(row.Id, row.ClientId, row.StatusKey,
                row.AttemptCount, row.NextAttemptAtUtc, row.LastErrorCode, row.CreatedAtUtc, row.DeliveredAtUtc)).ToArray());
        }).WithName("identityListOidcLogoutDeliveries").RequireFullNetPermission(IdentityOidcClientPermissions.Read);
        group.MapPost("/{id:guid}/retry", async (Guid id, HttpContext context, ICommandExecutor commands,
            ICommandTransaction transaction, OidcManagementAuditWriter audit, IClock clock, CancellationToken token) =>
        {
            if (!Guid.TryParse(context.User.FindFirstValue("sub"), out var actor)) return Results.Unauthorized();
            var count = await transaction.ExecuteAsync(async ct =>
            {
                var changed = await commands.ExecuteAsync(IdentityOidcLogoutSql.Retry,
                    IdentitySqlParameters.Create(("Id", id), ("Now", clock.UtcNow)), ct).ConfigureAwait(false);
                if (changed == 1) await audit.WriteAsync(actor, "oidc.logout_delivery_retried", $"delivery:{id:D}",
                    context.Connection.RemoteIpAddress?.ToString(), context.Request.Headers.UserAgent.ToString(), ct).ConfigureAwait(false);
                return changed;
            }, token).ConfigureAwait(false);
            return count == 1 ? Results.NoContent() : Results.Problem(statusCode: 409,
                extensions: new Dictionary<string, object?> { ["code"] = IdentityErrorCodes.OidcDeliveryNotFailed });
        }).WithName("identityRetryOidcLogoutDelivery").RequireFullNetPermission(IdentityOidcClientPermissions.Update);
    }
}
