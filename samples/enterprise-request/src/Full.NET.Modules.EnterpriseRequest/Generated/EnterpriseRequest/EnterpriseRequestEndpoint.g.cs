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

internal static class EnterpriseRequestEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/enterprise_request/enterprise-requests")
            .WithTags("EnterpriseRequestEnterpriseRequests");

            group.MapGet("/", async (
                ClaimsPrincipal principal,
                int? page,
                int? pageSize,
                [AsParameters] EnterpriseRequestListQuery query,
                EnterpriseRequestQueryService queries,
                IApiResultMapper mapper,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                                foreach (var pair in httpContext.Request.Query)
                        {
                            if (!EnterpriseRequestListQuery.QueryNames.Contains(pair.Key) || pair.Value.Count != 1
                                || !EnterpriseRequestListQuery.IsWireValueValid(pair.Key, pair.Value[0]))
                                return mapper.Map(Result<PagedResult<EnterpriseRequestResponse>>.Failure(EnterpriseRequestListQuery.Invalid()), httpContext);
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
        .WithName("enterpriseRequestListEnterpriseRequests")
        .Produces<PagedResult<EnterpriseRequestResponse>>(
            StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            EnterpriseRequestPermissions.Read));

            group.MapGet("/{enterpriseRequestId:guid}", async (
                Guid enterpriseRequestId,
                ClaimsPrincipal principal,
                EnterpriseRequestQueryService queries,
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
                        enterpriseRequestId,
                        actorUserId,
                        isSuperAdministrator,
                        cancellationToken)
                    .ConfigureAwait(false);
                return mapper.Map(result, httpContext);
            })
        .WithName("enterpriseRequestGetEnterpriseRequest")
        .Produces<EnterpriseRequestResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            EnterpriseRequestPermissions.Read));

            group.MapPost("/", async (
                CreateEnterpriseRequestRequest request,
                ClaimsPrincipal principal,
                EnterpriseRequestManagementService service,
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
                    return mapper.Map(Result<EnterpriseRequestResponse>.Failure(
                        new Full.NET.Abstractions.Results.Error("enterprise_request.enterprise_requests.invalid_organization",
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
                    $"/api/v1/enterprise_request/enterprise-requests/{result.Value!.Id:D}",
                    result.Value);
            })
        .WithName("enterpriseRequestCreateEnterpriseRequest")
        .Produces<EnterpriseRequestResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            EnterpriseRequestPermissions.Create));


        group.MapPut("/{enterpriseRequestId:guid}", async (
            Guid enterpriseRequestId,
            UpdateEnterpriseRequestRequest request,
            ClaimsPrincipal principal,
            EnterpriseRequestManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveActorUserId(principal, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await service.UpdateAsync(
                    enterpriseRequestId,
                    request,
                    actorUserId,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("enterpriseRequestUpdateEnterpriseRequest")
        .Produces<EnterpriseRequestResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            EnterpriseRequestPermissions.Update));

        group.MapPost("/{enterpriseRequestId:guid}/delete", async (
            Guid enterpriseRequestId,
            DeleteEnterpriseRequestRequest request,
            ClaimsPrincipal principal,
            EnterpriseRequestManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveActorUserId(principal, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await service.DeleteAsync(
                    enterpriseRequestId, request,
                    actorUserId,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("enterpriseRequestDeleteEnterpriseRequest")
        .Produces<EnterpriseRequestResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            EnterpriseRequestPermissions.Disable));
        group.MapGet("/{enterpriseRequestId:guid}/aggregate", async (Guid enterpriseRequestId, ClaimsPrincipal principal, EnterpriseRequestAggregateService service, IApiResultMapper mapper, HttpContext httpContext, CancellationToken token) =>
        {
            if (!TryResolveActor(principal, out var actorUserId, out var isSuperAdministrator)) return Results.Unauthorized();
            return mapper.Map(await service.GetAsync(enterpriseRequestId, actorUserId, isSuperAdministrator, token), httpContext);
        }).WithName("enterpriseRequestGetEnterpriseRequestAggregate").Produces<EnterpriseRequestAggregateResponse>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status400BadRequest).RequireAuthorization(
            FullNetPermissionPolicies.For(EnterpriseRequestPermissions.Read), FullNetPermissionPolicies.For(EnterpriseRequestDetailPermissions.Read));
        group.MapPost("/aggregate", async (CreateEnterpriseRequestAggregateRequest request, ClaimsPrincipal principal, EnterpriseRequestAggregateService service, IApiResultMapper mapper, HttpContext httpContext, CancellationToken token) =>
        {
            if (!TryResolveActorUserId(principal, out var actorUserId)) return Results.Unauthorized();
            if (!TryResolveOrganizationUnitId(httpContext, out var organizationUnitId)) return mapper.Map(Result<EnterpriseRequestAggregateResponse>.Failure(EnterpriseRequestAggregateService.Invalid()), httpContext);
            return mapper.Map(await service.CreateAsync(request, actorUserId, organizationUnitId, token), httpContext);
        }).WithName("enterpriseRequestCreateEnterpriseRequestAggregate").Produces<EnterpriseRequestAggregateResponse>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status400BadRequest).RequireAuthorization(FullNetPermissionPolicies.For(EnterpriseRequestPermissions.Create));
        group.MapPut("/{enterpriseRequestId:guid}/aggregate", async (Guid enterpriseRequestId, UpdateEnterpriseRequestAggregateRequest request, ClaimsPrincipal principal, EnterpriseRequestAggregateService service, IApiResultMapper mapper, HttpContext httpContext, CancellationToken token) =>
        {
            if (!TryResolveActorUserId(principal, out var actorUserId)) return Results.Unauthorized();
            return mapper.Map(await service.UpdateAsync(enterpriseRequestId, request, actorUserId, token), httpContext);
        }).WithName("enterpriseRequestUpdateEnterpriseRequestAggregate").Produces<EnterpriseRequestAggregateResponse>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status400BadRequest).RequireAuthorization(
            FullNetPermissionPolicies.For(EnterpriseRequestPermissions.Update), FullNetPermissionPolicies.For(EnterpriseRequestDetailPermissions.Read));

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

public static class EnterpriseRequestGeneratedFeatureExtensions
{
    public static IServiceCollection AddGeneratedEnterpriseRequestFeature(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
#if FULLNET_AOT_COMPILE
        EnterpriseRequestGeneratedMaterializer.Register();
#endif
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<EnterpriseRequestQueryService>();
        services.TryAddScoped<EnterpriseRequestManagementService>();
        services.AddGeneratedEnterpriseRequestDetailFeature();
        services.TryAddScoped<EnterpriseRequestAggregateService>();
        if (!System.Linq.Enumerable.Any(services, descriptor => descriptor.ImplementationInstance is GeneratedCrudErrorResourceSource source && source.Prefix == "enterprise_request.enterprise_requests."))
            services.AddSingleton<IErrorResourceSource>(new GeneratedCrudErrorResourceSource("enterprise_request.enterprise_requests."));

        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                EnterpriseRequestJsonSerializerContext.Default));
        return services;
    }

    public static IEndpointRouteBuilder MapGeneratedEnterpriseRequestFeature(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        EnterpriseRequestEndpoint.Map(endpoints);
        return endpoints;
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CreateEnterpriseRequestRequest))]
[JsonSerializable(typeof(UpdateEnterpriseRequestRequest))]
[JsonSerializable(typeof(DeleteEnterpriseRequestRequest))]
[JsonSerializable(typeof(CreateEnterpriseRequestAggregateRequest))]
[JsonSerializable(typeof(UpdateEnterpriseRequestAggregateRequest))]
[JsonSerializable(typeof(EnterpriseRequestAggregateResponse))]
[JsonSerializable(typeof(EnterpriseRequestResponse))]
[JsonSerializable(typeof(PagedResult<EnterpriseRequestResponse>))]
internal partial class EnterpriseRequestJsonSerializerContext
    : JsonSerializerContext;
