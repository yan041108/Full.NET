using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Migrations.DbUp;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFullNetMigrations(this IServiceCollection services)
    {
        services.AddSingleton<IDatabaseMigrationRunner, DbUpMigrationRunner>();
        return services;
    }
}
