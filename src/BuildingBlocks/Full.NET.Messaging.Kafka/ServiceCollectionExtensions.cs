using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 可选注册 Kafka Provider；仅在 <see cref="KafkaMessagingOptions.Enabled"/> 为 true 时启动 Consumer Worker。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Kafka Provider 的全部 DI 服务、健康检查与 Consumer Worker；
    /// 仅在 <see cref="KafkaMessagingOptions.Enabled"/> 为 true 时启动常驻消费。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    /// <param name="configuration">应用配置，用于绑定 Kafka 与 Connect 回滚选项。</param>
    /// <param name="environmentName">当前环境名，用于校验 Kafka 选项是否与环境匹配。</param>
    /// <returns>链式返回 <paramref name="services"/>。</returns>
    public static IServiceCollection AddFullNetKafkaMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        services
            .AddOptions<KafkaMessagingOptions>()
            .BindConfiguration(KafkaMessagingOptions.SectionName)
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<KafkaMessagingOptions>, KafkaMessagingOptionsValidator>());

        services.AddFullNetKafkaDeliveryControl(configuration);

        services.TryAddSingleton<KafkaEnvelopeReader>();
        services.TryAddSingleton<KafkaOffsetCommitter>();
        services.TryAddSingleton<KafkaFailureClassifier>();
        services.TryAddSingleton<KafkaConsumerMessageProcessor>();
        services.TryAddSingleton<KafkaMessagingProducer>();
        services.TryAddSingleton<KafkaRetryRouter>();
        services.TryAddSingleton<KafkaDeadLetterPublisher>();
        services.TryAddSingleton<IKafkaReplayConsumerFactory, KafkaReplayConsumerFactory>();
        services.TryAddScoped<IKafkaReplayMessageProcessor, KafkaReplayMessageProcessor>();
        services.TryAddScoped<KafkaReplayService>();
        services.TryAddScoped<IKafkaReplayService>(provider => provider.GetRequiredService<KafkaReplayService>());
        services.TryAddScoped<ITrackedKafkaReplayService>(provider => provider.GetRequiredService<KafkaReplayService>());
        services.TryAddSingleton<KafkaHealthCheck>();
        services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                "kafka-messaging",
                sp => sp.GetRequiredService<KafkaHealthCheck>(),
                failureStatus: null,
                tags: ["ready"]));

        var options = configuration
                .GetSection(KafkaMessagingOptions.SectionName)
                .Get<KafkaMessagingOptions>()
            ?? new KafkaMessagingOptions();
        var validation = KafkaMessagingOptionsValidation.Validate(options, environmentName);
        if (validation.Failed)
        {
            throw new OptionsValidationException(
                KafkaMessagingOptions.SectionName,
                typeof(KafkaMessagingOptions),
                validation.Failures);
        }

        if (options.Enabled)
        {
            services.AddHostedService<KafkaConsumerWorker>();
            if (options.DeadLetterProjectionEnabled)
                services.AddHostedService<KafkaDeadLetterProjectionWorker>();
        }

        return services;
    }

    /// <summary>仅装配 API 控制面；不注册常驻消费者、生产者或后台服务。</summary>
    public static IServiceCollection AddFullNetKafkaDeliveryControl(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<KafkaMessagingOptions>().BindConfiguration(KafkaMessagingOptions.SectionName).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KafkaMessagingOptions>, KafkaMessagingOptionsValidator>());
        services.AddOptions<KafkaConnectRollbackOptions>().BindConfiguration(KafkaConnectRollbackOptions.SectionName).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KafkaConnectRollbackOptions>, KafkaConnectRollbackOptionsValidator>());
        services.TryAddSingleton<KafkaConsumerLagObserver>();
        services.TryAddSingleton<KafkaConnectAdminClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<KafkaConnectRollbackOptions>>().Value;
            return new KafkaConnectAdminClient(new Uri(options.ConnectBaseUri ?? "http://127.0.0.1:8083"),
                TimeSpan.FromSeconds(options.PrepareTimeoutSeconds));
        });
        services.TryAddSingleton<IKafkaConnectAdminClient>(provider => provider.GetRequiredService<KafkaConnectAdminClient>());
        services.TryAddSingleton<IKafkaReplayConsumerFactory, KafkaReplayConsumerFactory>();
        if (configuration.GetValue<bool>($"{KafkaConnectRollbackOptions.SectionName}:Enabled"))
        {
            services.RemoveAll<IEventDeliveryRollbackReadinessReader>();
            services.AddSingleton<IEventDeliveryRollbackReadinessReader, KafkaConnectEventDeliveryRollbackReadinessReader>();
            services.RemoveAll<IEventDeliveryCutoverProbe>();
            services.AddScoped<IEventDeliveryCutoverProbe, KafkaEventDeliveryCutoverProbe>();
        }
        return services;
    }

    /// <summary>
    /// 仅为 API/CLI 注册一次性范围重放能力，不启动常驻 Consumer Worker。
    /// </summary>
    public static IServiceCollection AddFullNetKafkaReplayOperations(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<KafkaMessagingOptions>()
            .BindConfiguration(KafkaMessagingOptions.SectionName);
        services.AddOptions<KafkaReplayOperationsOptions>()
            .BindConfiguration(KafkaReplayOperationsOptions.SectionName)
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<KafkaReplayOperationsOptions>,
            KafkaReplayOperationsOptionsValidator>());
        services.TryAddSingleton(serviceProvider =>
        {
            var replayOptions = serviceProvider
                .GetRequiredService<IOptions<KafkaReplayOperationsOptions>>()
                .Value;
            return new KafkaReplayExecutionPolicy(
                replayOptions.Enabled,
                replayOptions.MaximumSynchronousMessages,
                TimeSpan.FromSeconds(replayOptions.ExecutionTimeoutSeconds));
        });
        services.TryAddSingleton<KafkaEnvelopeReader>();
        services.TryAddSingleton<IKafkaReplayConsumerFactory, KafkaReplayConsumerFactory>();
        services.TryAddScoped<IKafkaReplayMessageProcessor, KafkaReplayMessageProcessor>();
        services.TryAddScoped<KafkaReplayService>();
        services.TryAddScoped<IKafkaReplayService>(provider => provider.GetRequiredService<KafkaReplayService>());
        services.TryAddScoped<ITrackedKafkaReplayService>(provider => provider.GetRequiredService<KafkaReplayService>());
        return services;
    }
}
