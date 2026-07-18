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
    /// <summary>
    /// 注册 Full.NET Dapper 数据边界与启动配置验证。
    /// </summary>
    /// <param name="services">宿主服务集合。</param>
    /// <param name="configuration">宿主最终配置。</param>
    /// <param name="environmentName">当前宿主环境名称。</param>
    /// <returns>原服务集合，便于链式装配。</returns>
    public static IServiceCollection AddFullNetDapper(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        SqlMapper.AddTypeHandler(new UtcDateTimeOffsetTypeHandler());
        var databaseSection = configuration.GetSection(DatabaseOptions.SectionName);
        var hasExplicitMySqlGuidStorageMode = databaseSection
            .GetSection(nameof(DatabaseOptions.MySqlGuidStorageMode))
            .Value is not null;
        services.AddOptions<DatabaseOptions>()
            .Bind(databaseSection)
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
            .Validate(
                options => Enum.IsDefined(options.MySqlGuidStorageMode),
                "MySqlGuidStorageMode must be a supported value.")
            .Validate(
                _ => !string.Equals(
                        environmentName,
                        "Production",
                        StringComparison.OrdinalIgnoreCase)
                    || hasExplicitMySqlGuidStorageMode,
                "MySqlGuidStorageMode must be explicitly configured in Production.")
            .ValidateOnStart();

        services.AddSingleton<DbConnectionFactory>();
        services.AddScoped<DbSession>();
        services.AddScoped<DapperSqlExecutor>();
        services.AddScoped<IQueryExecutor>(provider =>
            provider.GetRequiredService<DapperSqlExecutor>());
        services.AddScoped<ICommandExecutor>(provider =>
            provider.GetRequiredService<DapperSqlExecutor>());
        services.AddScoped<IMultiResultQueryExecutor>(provider =>
            provider.GetRequiredService<DapperSqlExecutor>());
        services.AddScoped<IOutboxWriter, DapperOutboxWriter>();
        services.AddScoped<IOutboxStore, DapperOutboxStore>();
        services.AddScoped<ICommandTransaction, DapperCommandTransaction>();
        return services;
    }
}
