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

/// <summary>
/// Full.NET Dapper 数据访问模块的依赖注入扩展方法集合，入口为
/// <see cref="AddFullNetDapper(IServiceCollection, IConfiguration)"/> 系列重载。
/// </summary>
/// <remarks>
/// <para><b>注册内容概览：</b></para>
/// <list type="number">
/// <item>Dapper TypeHandler 层：移除内置 Guid 映射并注册
/// <see cref="AssignedGuidTypeHandler"/>（RFC 9562 Big-Endian + 禁止 Empty）与
/// <see cref="UtcDateTimeOffsetTypeHandler"/>（双向 UTC 标准化）。</item>
/// <item>Options 层：绑定 <see cref="DatabaseOptions"/> 与 <see cref="MessagingOutboxOptions"/>，
/// 并执行生产环境强约束校验（MySQL 必须显式 Binary16 GuidStorageMode 等）。</item>
/// <item>基础设施层：<see cref="DbConnectionFactory"/>（Singleton）、
/// <see cref="DbSession"/>（Scoped，实现 <see cref="IDbTransactionCoordinator"/>）、
/// <see cref="DapperSqlExecutor"/>（Scoped，实现三个 Executor 接口）、
/// <see cref="DapperDatabaseSessionLock"/>（Singleton 分布式锁）。</item>
/// <item>Messaging 层：<see cref="DapperOutboxWriter"/>（Legacy）/
/// <see cref="DapperAppendOnlyOutboxWriter"/>（Append-Only）/
/// <see cref="DapperRoutedOutboxWriter"/>（按所有权路由，注册为 IOutboxWriter 接口）、
/// <see cref="DapperEventStreamOwnershipGate"/>（CAS 所有权门禁）、
/// <see cref="DapperOutboxStore"/>（领取/续租/完成/死信）、
/// <see cref="DapperIntegrationEventInbox"/>（消费端去重）。</item>
/// <item>可观测性层：OpenTelemetry Meter（DapperTelemetry）、
/// 两类健康检查（database-connectivity + database-schema-contract）。</item>
/// </list>
/// </remarks>
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

#if !FULLNET_AOT_COMPILE
        // Dapper 内置 Guid 类型映射优先于 TypeHandler，必须先移除才会经过空值门禁。
        SqlMapper.RemoveTypeMap(typeof(Guid));
        SqlMapper.AddTypeHandler(new AssignedGuidTypeHandler());
        SqlMapper.AddTypeHandler(new UtcDateTimeOffsetTypeHandler());
#else
        DapperAotInfrastructureRegistration.Register();
#endif
        var databaseSection = configuration.GetSection(DatabaseOptions.SectionName);
        var hasExplicitMySqlGuidStorageMode = databaseSection
            .GetSection(nameof(DatabaseOptions.MySqlGuidStorageMode))
            .Value is not null;
        // BindConfiguration 从 DI 解析配置；固定注册调用方传入实例，保持独立 ServiceCollection 与宿主行为一致。
        services.AddSingleton(configuration);
        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
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

        services.AddSingleton<
            IValidateOptions<DatabaseCapacityOptions>,
            DatabaseCapacityOptionsValidator>();
        services.AddOptions<DatabaseCapacityOptions>()
            .BindConfiguration(DatabaseCapacityOptions.SectionName)
            .ValidateOnStart();

        services.AddSingleton<DbConnectionFactory>();
        services.AddSingleton<IDbConnectionFactory>(provider =>
            provider.GetRequiredService<DbConnectionFactory>());
        services.AddSingleton<DatabaseConnectionTelemetry>();
        services.AddSingleton<DatabaseAdmissionGate>();
        services.AddScoped<DatabaseAdmissionPriorityScope>();
        services.AddScoped<IDatabaseAdmissionPriorityScope>(provider =>
            provider.GetRequiredService<DatabaseAdmissionPriorityScope>());
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
            .BindConfiguration(MessagingOutboxOptions.SectionName);
        var outboxCommandPath = DapperOutboxCommandPathPolicy.Resolve(
            configuration,
            environmentName);
        services.AddScoped(provider =>
            ActivatorUtilities.CreateInstance<DapperOutboxWriter>(
                provider,
                outboxCommandPath));
        services.AddScoped(provider =>
            ActivatorUtilities.CreateInstance<DapperAppendOnlyOutboxWriter>(
                provider,
                outboxCommandPath));
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
            .WithMetrics(metrics => metrics
                .AddMeter(DapperTelemetry.MeterName)
                .AddMeter(DatabaseConnectionTelemetry.MeterName));
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
