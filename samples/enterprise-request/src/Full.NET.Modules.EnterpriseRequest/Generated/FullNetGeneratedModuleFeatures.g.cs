#nullable enable

using System;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.EnterpriseRequest.Generated;

public static class FullNetGeneratedModuleFeatureExtensions
{
    public static IServiceCollection AddFullNetGeneratedModuleFeatures(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddGeneratedEnterpriseRequestFeature();
        return services;
    }

    public static IEndpointRouteBuilder MapFullNetGeneratedModuleFeatures(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGeneratedEnterpriseRequestFeature();
        return endpoints;
    }
}
