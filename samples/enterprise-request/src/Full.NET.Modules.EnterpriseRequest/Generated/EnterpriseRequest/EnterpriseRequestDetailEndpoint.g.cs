#nullable enable

using System;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.EnterpriseRequest.Generated;

internal static class EnterpriseRequestDetailEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/enterprise_request/enterprise-request-lines")
            .WithTags("EnterpriseRequestEnterpriseRequestLines");

            group.MapGet("/", async (
                ClaimsPrincipal principal,
                int? page,
                int? pageSize,
                [AsParameters] EnterpriseRequestDetailListQuery query,
                EnterpriseRequestDetailQueryService queries,
                IApiResultMapper mapper,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                                foreach (var pair in httpContext.Request.Query)
                        {
                            if (!EnterpriseRequestDetailListQuery.QueryNames.Contains(pair.Key) || pair.Value.Count != 1
                                || !EnterpriseRequestDetailListQuery.IsWireValueValid(pair.Key, pair.Value[0]))
                                return mapper.Map(Result<PagedResult<EnterpriseRequestDetailResponse>>.Failure(EnterpriseRequestDetailListQuery.Invalid()), httpContext);
                        }
                if (!TryResolveActor(
                        principal,
                        out var actorUserId,
                        out var isSuperAdministrator))
                {
                    return Results.Unauthorized();
                }

                var result = await queries.ListAsync(
                        actorUserId,
                        isSuperAdministrator,
                        page ?? 1,
                        pageSize ?? 20,
                        cancellationToken, query)
                    .ConfigureAwait(false);
                return mapper.Map(result, httpContext);
            })
        .WithName("enterpriseRequestListEnterpriseRequestLines")
        .Produces<PagedResult<EnterpriseRequestDetailResponse>>(
            StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            EnterpriseRequestDetailPermissions.Read));

            group.MapGet("/{enterpriseRequestDetailId:guid}", async (
                Guid enterpriseRequestDetailId,
                ClaimsPrincipal principal,
                EnterpriseRequestDetailQueryService queries,
                IApiResultMapper mapper,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                if (!TryResolveActor(
                        principal,
                        out var actorUserId,
                        out var isSuperAdministrator))
                {
                    return Results.Unauthorized();
                }

                var result = await queries.GetByIdAsync(
                        enterpriseRequestDetailId,
                        actorUserId,
                        isSuperAdministrator,
                        cancellationToken)
                    .ConfigureAwait(false);
                return mapper.Map(result, httpContext);
            })
        .WithName("enterpriseRequestGetEnterpriseRequestDetail")
        .Produces<EnterpriseRequestDetailResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            EnterpriseRequestDetailPermissions.Read));

            group.MapPost("/", async (
                CreateEnterpriseRequestDetailRequest request,
                ClaimsPrincipal principal,
                EnterpriseRequestDetailManagementService service,
                IApiResultMapper mapper,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                if (!TryResolveActorUserId(principal, out var actorUserId))
                {
                    return Results.Unauthorized();
                }

                if (!TryResolveOrganizationUnitId(
                        httpContext,
                        out var organizationUnitId))
                {
                    return mapper.Map(Result<EnterpriseRequestDetailResponse>.Failure(
                        new Full.NET.Abstractions.Results.Error("enterprise_request.enterprise_request_lines.invalid_organization",
                            "A valid organization unit header is required.", Full.NET.Abstractions.Results.ErrorType.Validation)), httpContext);
                }

                var result = await service.CreateAsync(
                        request,
                        actorUserId,
                        organizationUnitId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    return mapper.Map(result, httpContext);
                }

                return Results.Created(
                    $"/api/v1/enterprise_request/enterprise-request-lines/{result.Value!.Id:D}",
                    result.Value);
            })
        .WithName("enterpriseRequestCreateEnterpriseRequestDetail")
        .Produces<EnterpriseRequestDetailResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            EnterpriseRequestDetailPermissions.Create));


        group.MapPut("/{enterpriseRequestDetailId:guid}", async (
            Guid enterpriseRequestDetailId,
            UpdateEnterpriseRequestDetailRequest request,
            ClaimsPrincipal principal,
            EnterpriseRequestDetailManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveActorUserId(principal, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await service.UpdateAsync(
                    enterpriseRequestDetailId,
                    request,
                    actorUserId,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("enterpriseRequestUpdateEnterpriseRequestDetail")
        .Produces<EnterpriseRequestDetailResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            EnterpriseRequestDetailPermissions.Update));

        group.MapPost("/{enterpriseRequestDetailId:guid}/delete", async (
            Guid enterpriseRequestDetailId,
            DeleteEnterpriseRequestDetailRequest request,
            ClaimsPrincipal principal,
            EnterpriseRequestDetailManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveActorUserId(principal, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await service.DeleteAsync(
                    enterpriseRequestDetailId, request,
                    actorUserId,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("enterpriseRequestDeleteEnterpriseRequestDetail")
        .Produces<EnterpriseRequestDetailResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            EnterpriseRequestDetailPermissions.Disable));


    }

    private static bool TryResolveActorUserId(
        ClaimsPrincipal principal,
        out Guid actorUserId) =>
        Guid.TryParse(
            principal.FindFirstValue(
                FullNetIdentityClaimTypes.Subject),
            out actorUserId);

        private static bool TryResolveActor(
            ClaimsPrincipal principal,
            out Guid actorUserId,
            out bool isSuperAdministrator)
        {
            actorUserId = default;
            isSuperAdministrator = bool.TryParse(
                principal.FindFirstValue(
                    FullNetIdentityClaimTypes.SuperAdministrator),
                out var enabled)
                && enabled;
            return Guid.TryParse(
                principal.FindFirstValue(
                    FullNetIdentityClaimTypes.Subject),
                out actorUserId);
        }

        private static bool TryResolveOrganizationUnitId(
            HttpContext httpContext,
            out Guid organizationUnitId)
        {
            organizationUnitId = default;
            return Guid.TryParse(
                httpContext.Request.Headers[
                    OrganizationRequestHeaders.OrganizationUnitId],
                out organizationUnitId) && organizationUnitId != Guid.Empty;
        }
}

public static class EnterpriseRequestDetailGeneratedFeatureExtensions
{
    public static IServiceCollection AddGeneratedEnterpriseRequestDetailFeature(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
#if FULLNET_AOT_COMPILE
        EnterpriseRequestDetailGeneratedMaterializer.Register();
#endif
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<EnterpriseRequestDetailQueryService>();
        services.TryAddScoped<EnterpriseRequestDetailManagementService>();

        if (!System.Linq.Enumerable.Any(services, descriptor => descriptor.ImplementationInstance is GeneratedCrudErrorResourceSource source && source.Prefix == "enterprise_request.enterprise_request_lines."))
            services.AddSingleton<IErrorResourceSource>(new GeneratedCrudErrorResourceSource("enterprise_request.enterprise_request_lines."));

        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                EnterpriseRequestDetailJsonSerializerContext.Default));
        return services;
    }

    public static IEndpointRouteBuilder MapGeneratedEnterpriseRequestDetailFeature(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        EnterpriseRequestDetailEndpoint.Map(endpoints);
        return endpoints;
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CreateEnterpriseRequestDetailRequest))]
[JsonSerializable(typeof(UpdateEnterpriseRequestDetailRequest))]
[JsonSerializable(typeof(DeleteEnterpriseRequestDetailRequest))]
[JsonSerializable(typeof(EnterpriseRequestDetailResponse))]
[JsonSerializable(typeof(PagedResult<EnterpriseRequestDetailResponse>))]
internal partial class EnterpriseRequestDetailJsonSerializerContext
    : JsonSerializerContext;
