using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using global::Dapper;

namespace Full.NET.Data.Dapper;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFullNetDapper(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        SqlMapper.AddTypeHandler(new UtcDateTimeOffsetTypeHandler());
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    options.ConnectionString = configuration.GetConnectionString(
                        options.ConnectionName) ?? string.Empty;
                }
            })
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                "A database connection string is required.")
            .Validate(
                options => options.CommandTimeoutSeconds > 0,
                "CommandTimeoutSeconds must be greater than zero.")
            .ValidateOnStart();

        services.AddSingleton<DbConnectionFactory>();
        services.AddScoped<DbSession>();
        services.AddScoped<DapperSqlExecutor>();
        services.AddScoped<IQueryExecutor>(provider =>
            provider.GetRequiredService<DapperSqlExecutor>());
        services.AddScoped<ICommandExecutor>(provider =>
            provider.GetRequiredService<DapperSqlExecutor>());
        services.AddScoped<IOutboxWriter, DapperOutboxWriter>();
        services.AddScoped<IOutboxStore, DapperOutboxStore>();
        services.AddScoped<ICommandTransaction, DapperCommandTransaction>();
        return services;
    }
}
