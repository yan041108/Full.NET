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
            .Bind(configuration.GetSection(KafkaMessagingOptions.SectionName))
            .ValidateOnStart();
        services
            .AddOptions<KafkaConnectRollbackOptions>()
            .Bind(configuration.GetSection(KafkaConnectRollbackOptions.SectionName));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<KafkaMessagingOptions>, KafkaMessagingOptionsValidator>());

        services.TryAddSingleton<RollbackControlPlaneFenceRegistry>();
        services.TryAddSingleton<KafkaConsumerLagObserver>();
        services.TryAddSingleton<KafkaConnectAdminClient>(serviceProvider =>
        {
            var rollbackOptions = serviceProvider
                .GetRequiredService<IOptions<KafkaConnectRollbackOptions>>()
                .Value;
            var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(
                    Math.Max(rollbackOptions.PrepareTimeoutSeconds, 30)),
            };
            if (!string.IsNullOrWhiteSpace(rollbackOptions.ConnectBaseUri))
            {
                httpClient.BaseAddress = new Uri(rollbackOptions.ConnectBaseUri);
            }

            return new KafkaConnectAdminClient(httpClient);
        });

        var rollbackOptions = new KafkaConnectRollbackOptions();
        configuration.GetSection(KafkaConnectRollbackOptions.SectionName).Bind(rollbackOptions);
        if (rollbackOptions.Enabled)
        {
            services.RemoveAll<IEventDeliveryRollbackReadinessReader>();
            services.TryAddSingleton<
                IEventDeliveryRollbackReadinessReader,
                KafkaConnectEventDeliveryRollbackReadinessReader>();
        }

        services.TryAddSingleton<KafkaEnvelopeReader>();
        services.TryAddSingleton<KafkaOffsetCommitter>();
        services.TryAddSingleton<KafkaFailureClassifier>();
        services.TryAddSingleton<KafkaConsumerMessageProcessor>();
        services.TryAddSingleton<KafkaMessagingProducer>();
        services.TryAddSingleton<KafkaRetryRouter>();
        services.TryAddSingleton<KafkaDeadLetterPublisher>();
        services.TryAddSingleton<IKafkaReplayConsumerFactory, KafkaReplayConsumerFactory>();
        services.TryAddScoped<IKafkaReplayMessageProcessor, KafkaReplayMessageProcessor>();
        services.TryAddScoped<IKafkaReplayService, KafkaReplayService>();
        services.TryAddSingleton<KafkaHealthCheck>();
        services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                "kafka-messaging",
                sp => sp.GetRequiredService<KafkaHealthCheck>(),
                failureStatus: null,
                tags: ["ready"]));

        var options = new KafkaMessagingOptions();
        configuration.GetSection(KafkaMessagingOptions.SectionName).Bind(options);
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
            .Bind(configuration.GetSection(KafkaMessagingOptions.SectionName));
        services.AddOptions<KafkaReplayOperationsOptions>()
            .Bind(configuration.GetSection(KafkaReplayOperationsOptions.SectionName))
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
        services.TryAddScoped<IKafkaReplayService, KafkaReplayService>();
        return services;
    }
}
