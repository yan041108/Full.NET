using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Hosting.Serialization;

public static class FullNetJsonOptionsExtensions
{
    public static IServiceCollection AddFullNetJson(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
        });

        return services;
    }
}
