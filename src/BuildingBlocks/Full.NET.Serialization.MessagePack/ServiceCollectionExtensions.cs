using Full.NET.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Serialization.MessagePack;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFullNetMessagePack(
        this IServiceCollection services)
    {
        services.AddSingleton<MessagePackIntegrationEventSerializer>();
        services.AddSingleton<IIntegrationEventSerializer>(provider =>
            provider.GetRequiredService<MessagePackIntegrationEventSerializer>());
        return services;
    }
}
