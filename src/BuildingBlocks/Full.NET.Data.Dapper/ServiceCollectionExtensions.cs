using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper.Health;
using Full.NET.Data.Dapper.Inbox;
using Full.NET.Data.Dapper.Outbox;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using global::Dapper;

namespace Full.NET.Data.Dapper;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 从宿主配置推断环境并注册 Full.NET Dapper 数据边界。
    /// </summary>
    /// <remarks>
    /// 环境键缺失时按 Production 处理，避免旧重载绕过生产配置门禁。
    /// </remarks>
    /// <param name="services">宿主服务集合。</param>
    /// <param name="configuration">包含宿主环境键的最终配置。</param>
    /// <returns>原服务集合，便于链式装配。</returns>
    public static IServiceCollection AddFullNetDapper(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var environmentName = configuration[HostDefaults.EnvironmentKey];
        return services.AddFullNetDapper(
            configuration,
            string.IsNullOrWhiteSpace(environmentName)
                ? Environments.Production
                : environmentName);
    }

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

        // Dapper 内置 Guid 类型映射优先于 TypeHandler，必须先移除才会经过空值门禁。
        SqlMapper.RemoveTypeMap(typeof(Guid));
        SqlMapper.AddTypeHandler(new AssignedGuidTypeHandler());
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
                options => Enum.IsDefined(options.Provider),
                "Provider must be a supported value.")
            .Validate(
                options => Enum.IsDefined(options.MySqlGuidStorageMode),
                "MySqlGuidStorageMode must be a supported value.")
            .Validate(
                _ => !string.Equals(
                        environmentName,
                        Environments.Production,
                        StringComparison.OrdinalIgnoreCase)
                    || hasExplicitMySqlGuidStorageMode,
                "MySqlGuidStorageMode must be explicitly configured in Production.")
            .Validate(
                options => !string.Equals(
                        environmentName,
                        Environments.Production,
                        StringComparison.OrdinalIgnoreCase)
                    || options.Provider != DatabaseProvider.MySql
                    || options.MySqlGuidStorageMode == MySqlGuidStorageMode.Binary16,
                "LegacyChar36 is not permitted in Production; use Binary16.")
            .ValidateOnStart();

        services.AddSingleton<DbConnectionFactory>();
        services.AddSingleton<IDatabaseSessionLock, DapperDatabaseSessionLock>();
        services.AddScoped<DbSession>();
        services.AddScoped<IDbTransactionCoordinator>(provider =>
            provider.GetRequiredService<DbSession>());
        services.AddScoped<DapperSqlExecutor>();
        services.AddScoped<IQueryExecutor>(provider =>
            provider.GetRequiredService<DapperSqlExecutor>());
        services.AddScoped<ICommandExecutor>(provider =>
            provider.GetRequiredService<DapperSqlExecutor>());
        services.AddScoped<IMultiResultQueryExecutor>(provider =>
            provider.GetRequiredService<DapperSqlExecutor>());
        services.AddOptions<MessagingOutboxOptions>()
            .Bind(configuration.GetSection(MessagingOutboxOptions.SectionName));
        services.AddScoped<DapperOutboxWriter>();
        services.AddScoped<DapperAppendOnlyOutboxWriter>();
        services.AddScoped<IEventStreamOwnershipGate, DapperEventStreamOwnershipGate>();
        services.AddScoped<IEventDeliveryProducerFencePositionReader, DapperEventDeliveryProducerFencePositionReader>();
        services.TryAddScoped<IEffectiveEventDeliveryOwnerResolver, LegacyPollingEventDeliveryOwnerResolver>();
        // 不单独公开 DapperRoutedOutboxWriter 具体类型，业务只依赖 IOutboxWriter。
        // 精简宿主使用 LegacyPolling 兼容解析器；装配 Messaging 模块后会替换为持久化所有权解析器。
        services.AddScoped<IOutboxWriter>(provider =>
            ActivatorUtilities.CreateInstance<DapperRoutedOutboxWriter>(
                provider,
                provider.GetRequiredService<DapperOutboxWriter>(),
                provider.GetRequiredService<DapperAppendOnlyOutboxWriter>()));
        services.AddScoped<DapperIntegrationEventInbox>();
        services.AddScoped<IIntegrationEventInbox>(provider =>
            provider.GetRequiredService<DapperIntegrationEventInbox>());
        services.AddScoped<DapperOutboxStore>();
        services.AddScoped<IOutboxStore>(provider =>
            provider.GetRequiredService<DapperOutboxStore>());
        services.AddScoped<IOutboxBacklogReader>(provider =>
            provider.GetRequiredService<DapperOutboxStore>());
        services.AddScoped<IOutboxRetentionStore>(provider =>
            provider.GetRequiredService<DapperOutboxStore>());
        services.AddScoped<ICommandTransaction, DapperCommandTransaction>();
        services
            .AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddMeter(DapperTelemetry.MeterName));
        services.AddHealthChecks()
            .AddCheck<DatabaseConnectivityHealthCheck>(
                "database-connectivity",
                tags: ["ready"])
            .AddCheck<DatabaseSchemaHealthCheck>(
                "database-schema-contract",
                tags: ["startup"]);
        return services;
    }

    /// <summary>
    /// 注册 MySQL UUID 应用模式与数据库 Contract schema 的启动前一致性门禁。
    /// </summary>
    /// <remarks>
    /// API 与 Worker 必须注册该门禁；Migrator 需要连接旧 schema 执行 009，因此不得注册。
    /// </remarks>
    /// <param name="services">宿主服务集合。</param>
    /// <returns>原服务集合，便于链式装配。</returns>
    public static IServiceCollection AddFullNetDatabaseSchemaModeGuard(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHostedService<MySqlSchemaModeStartupValidator>();
        return services;
    }
}
