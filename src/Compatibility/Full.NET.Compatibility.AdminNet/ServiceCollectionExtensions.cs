using Full.NET.Hosting.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Compatibility.AdminNet;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAdminNetCompatibility(
        this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<
            IApiResultMapper,
            AdminNetApiResultMapper>());
        return services;
    }
}
