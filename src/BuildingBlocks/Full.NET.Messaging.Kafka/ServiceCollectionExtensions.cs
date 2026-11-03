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

        AddKafkaOptions(services, configuration);
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

        var section = configuration.GetSection(KafkaMessagingOptions.SectionName);
        var options = new KafkaMessagingOptions();
        if (section.GetSection(nameof(KafkaMessagingOptions.RetryStages)).Exists()) options.RetryStages = [];
        section.Bind(options);
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
            if (configuration.GetValue<bool>($"{KafkaConnectRollbackOptions.SectionName}:Enabled"))
                services.AddHostedService<KafkaDeliveryControlReconciler>();
            services.AddHostedService<KafkaConsumerWorker>();
            if (options.DeadLetterProjectionEnabled)
                services.AddHostedService<KafkaDeadLetterProjectionWorker>();
        }

        return services;
    }

    /// <summary>注册 Worker 专用只读观测；禁用时仅报告 disabled，不发起数据库或外部查询。</summary>
    /// <param name="services">Worker 服务集合。</param>
    /// <param name="configuration">采集预算与专用试点资源配置。</param>
    /// <returns>当前服务集合。</returns>
    public static IServiceCollection AddFullNetCdcMonitoring(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddFullNetKafkaDeliveryControl(configuration);
        services.AddOptions<KafkaCdcMonitoringOptions>().BindConfiguration(KafkaCdcMonitoringOptions.SectionName).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KafkaCdcMonitoringOptions>, KafkaCdcMonitoringOptionsValidator>());
        services.AddHostedService<KafkaCdcMonitoringWorker>();
        return services;
    }

    /// <summary>仅装配 API 控制面；不注册常驻消费者、生产者或后台服务。</summary>
    public static IServiceCollection AddFullNetKafkaDeliveryControl(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        AddKafkaOptions(services, configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KafkaMessagingOptions>, KafkaMessagingOptionsValidator>());
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(KafkaControlBindingMarker)))
        {
            services.AddSingleton(new KafkaControlBindingMarker());
            services.AddOptions<KafkaConnectRollbackOptions>().BindConfiguration(KafkaConnectRollbackOptions.SectionName).ValidateOnStart();
        }
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
            services.TryAddSingleton<KafkaConnectEventDeliveryRollbackReadinessReader>();
            services.AddSingleton<IEventDeliveryRollbackReadinessReader>(provider => provider.GetRequiredService<KafkaConnectEventDeliveryRollbackReadinessReader>());
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
        AddKafkaOptions(services, configuration);
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

    private static void AddKafkaOptions(IServiceCollection services, IConfiguration configuration)
    {
        // API/Worker 的多个能力入口共用同一个选项；重复绑定会把数组再次追加，破坏重试顺序。
        if (services.Any(descriptor => descriptor.ServiceType == typeof(KafkaOptionsBindingMarker))) return;
        services.AddSingleton(new KafkaOptionsBindingMarker());
        services.AddOptions<KafkaMessagingOptions>()
            .Configure(options =>
            {
                // 配置数组替代默认阶段；未配置时保留官方默认值，重载时重新按当前配置判断。
                if (configuration.GetSection(KafkaMessagingOptions.SectionName).GetSection(nameof(KafkaMessagingOptions.RetryStages)).Exists())
                    options.RetryStages = [];
            })
            .BindConfiguration(KafkaMessagingOptions.SectionName)
            .ValidateOnStart();
    }

    private sealed class KafkaOptionsBindingMarker;
    private sealed class KafkaControlBindingMarker;
}
