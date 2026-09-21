using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.ManageTenantMembers;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/tenant-members")
            .WithTags("IdentityTenantMembers");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            string? status,
            TenantMembershipQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListMembersAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    status,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityListTenantMembers")
        .Produces<PagedResult<TenantMemberResponse>>(StatusCodes.Status200OK)
        .RequireFullNetPermission(IdentityTenantMembershipPermissions.Read);

        group.MapGet("/{memberId:guid}", async (
            Guid memberId,
            TenantMembershipQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetMemberByIdAsync(memberId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityGetTenantMember")
        .Produces<TenantMemberResponse>(StatusCodes.Status200OK)
        .RequireFullNetPermission(IdentityTenantMembershipPermissions.Read);

        group.MapGet("/invitations", async (
            int? page,
            int? pageSize,
            string? status,
            TenantMembershipQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListInvitationsAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    status,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityListTenantInvitations")
        .Produces<PagedResult<TenantInvitationResponse>>(StatusCodes.Status200OK)
        .RequireFullNetPermission(IdentityTenantMembershipPermissions.Read);

        group.MapPost("/invitations", async (
            CreateTenantInvitationRequest request,
            TenantMembershipManagementService service,
            ICurrentSessionAuthorization sessions,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var actor = await sessions.AuthorizeAsync(
                    IdentityTenantMembershipPermissions.Invite,
                    cancellationToken)
                .ConfigureAwait(false);
            if (actor is null)
            {
                return Results.Forbid();
            }

            var result = await service.InviteAsync(actor.UserId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityCreateTenantInvitation")
        .Produces<CreateTenantInvitationResult>(StatusCodes.Status200OK)
        .RequireFullNetPermission(IdentityTenantMembershipPermissions.Invite);

        group.MapPost("/provision", async (
            ProvisionTenantMemberRequest request,
            TenantMemberProvisionService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ProvisionAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityProvisionTenantMember")
        .Produces<TenantMemberResponse>(StatusCodes.Status200OK)
        .RequireFullNetPermission(IdentityTenantMembershipPermissions.Provision);

        group.MapPost("/invitations/{invitationId:guid}/revoke", async (
            Guid invitationId,
            TenantMembershipManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.RevokeInvitationAsync(invitationId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityRevokeTenantInvitation")
        .Produces<TenantInvitationResponse>(StatusCodes.Status200OK)
        .RequireFullNetPermission(IdentityTenantMembershipPermissions.RevokeInvitation);

        group.MapPut("/{memberId:guid}", async (
            Guid memberId,
            UpdateTenantMemberRequest request,
            TenantMembershipManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateMemberAsync(memberId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityUpdateTenantMember")
        .Produces<TenantMemberResponse>(StatusCodes.Status200OK)
        .RequireFullNetPermission(IdentityTenantMembershipPermissions.Update);

        group.MapDelete("/{memberId:guid}", async (
            Guid memberId,
            int version,
            TenantMembershipManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.RemoveMemberAsync(memberId, version, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityRemoveTenantMember")
        .Produces<TenantMemberResponse>(StatusCodes.Status200OK)
        .RequireFullNetPermission(IdentityTenantMembershipPermissions.Remove);
    }
}
