#nullable enable

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Acme.Modules.Catalog.Generated;

internal static class ProductEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/catalog/products")
            .WithTags("CatalogProducts");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            ProductQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("catalogListProducts")
        .Produces<PagedResult<ProductResponse>>(
            StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ProductPermissions.Read));

        group.MapGet("/{productId:guid}", async (
            Guid productId,
            ProductQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(
                    productId,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("catalogGetProduct")
        .Produces<ProductResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ProductPermissions.Read));

        group.MapPost("/", async (
            CreateProductRequest request,
            ProductManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/catalog/products/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("catalogCreateProduct")
        .Produces<ProductResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ProductPermissions.Write));

        group.MapPut("/{productId:guid}", async (
            Guid productId,
            UpdateProductRequest request,
            ProductManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(
                    productId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("catalogUpdateProduct")
        .Produces<ProductResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ProductPermissions.Write));

        group.MapPost("/{productId:guid}/disable", async (
            Guid productId,
            DisableProductRequest request,
            ProductManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(
                    productId, request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("catalogDisableProduct")
        .Produces<ProductResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ProductPermissions.Write));
    }
}

public static class ProductGeneratedFeatureExtensions
{
    public static IServiceCollection AddGeneratedProductFeature(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<ProductQueryService>();
        services.TryAddScoped<ProductManagementService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                ProductJsonSerializerContext.Default));
        return services;
    }

    public static IEndpointRouteBuilder MapGeneratedProductFeature(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ProductEndpoint.Map(endpoints);
        return endpoints;
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CreateProductRequest))]
[JsonSerializable(typeof(UpdateProductRequest))]
[JsonSerializable(typeof(DisableProductRequest))]
[JsonSerializable(typeof(ProductResponse))]
[JsonSerializable(typeof(PagedResult<ProductResponse>))]
internal partial class ProductJsonSerializerContext
    : JsonSerializerContext;
