using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.ManageTenantOwnership;

/// <summary>企业内所有者职责及原操作回执；恢复读取也只返回操作者本人在当前企业的事实。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/tenant-members/ownership").WithTags("IdentityTenantOwnership");
        group.MapGet("/", async (TenantOwnershipService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            mapper.Map(await service.ReadAsync(token).ConfigureAwait(false), http))
            .WithName("identityGetTenantOwnership").Produces<TenantOwnershipStateResponse>()
            .RequireFullNetPermission(IdentityTenantMembershipPermissions.Read);
        group.MapGet("/operations/{operationId:guid}", async (Guid operationId, TenantOwnershipService service,
            IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            mapper.Map(await service.ReadReceiptAsync(operationId, token).ConfigureAwait(false), http))
            .WithName("identityGetTenantOwnerTransferReceipt").Produces<TenantOwnerTransferReceipt>()
            .RequireFullNetPermission(IdentityTenantMembershipPermissions.Read);
        group.MapPost("/transfer", async (TransferTenantOwnerRequest request, TenantOwnershipService service,
            IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            mapper.Map(await service.TransferAsync(request, http.User, token).ConfigureAwait(false), http))
            .WithName("identityTransferTenantOwner").Produces<TenantOwnerTransferReceipt>()
            .RequireFullNetPermission(IdentityTenantMembershipPermissions.TransferOwnership)
            .RequireRateLimiting("identity-tenant-owner-write");
    }
}
