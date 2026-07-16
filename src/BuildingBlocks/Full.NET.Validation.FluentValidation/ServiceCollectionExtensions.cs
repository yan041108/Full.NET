using Full.NET.Abstractions.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Validation.FluentValidation;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFullNetFluentValidation(
        this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
            typeof(IDispatchBehavior<,>),
            typeof(FluentValidationBehavior<,>)));
        return services;
    }
}
