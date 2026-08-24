using Full.NET.Abstractions.Messaging;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Messaging;

/// <summary>
/// Testing 环境下为 Native Kafka Replay E2E 注册最小 inbox 测试订阅与 Topic 目录。
/// </summary>
/// <remarks>
/// 仅在 Environment=Testing 时启用；JIT 集成测试仍通过工厂 <c>ConfigureTestServices</c> 注入，
/// 本 Harness 专供已发布 Native 外部进程无法改写 DI 的场景。
/// </remarks>
internal static class MessagingNativeKafkaReplayTestHarness
{
    internal const string TestEventType = "fullnet.messaging.outbox.test.event";
    internal const int TestSchemaVersion = 1;
    internal const string TopicCode = "messaging.inbox-test.v1";
    internal const string ConsumerName = "fullnet.messaging.inbox.test";

    internal static bool IsTestingEnvironment(IConfiguration configuration) =>
        string.Equals(
            configuration["DOTNET_ENVIRONMENT"],
            "Testing",
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            configuration["ASPNETCORE_ENVIRONMENT"],
            "Testing",
            StringComparison.OrdinalIgnoreCase);

    internal static void RegisterIfTesting(
        IServiceCollection services,
        IConfiguration configuration)
    {
        if (!IsTestingEnvironment(configuration))
        {
            return;
        }

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(IntegrationEventTopicDefinition)
                && descriptor.ImplementationInstance is IntegrationEventTopicDefinition existing
                && existing.TopicCode == TopicCode))
        {
            services.AddSingleton(
                IntegrationEventTopicDefinition.Create(
                    TopicCode,
                    TestEventType,
                    TestSchemaVersion,
                    EventDeliveryOwner.CdcKafka));
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                IIntegrationEventSubscription,
                NativeKafkaReplayNoOpSubscription>());
    }

    private sealed class NativeKafkaReplayNoOpSubscription : IIntegrationEventSubscription
    {
        public string ConsumerName => MessagingNativeKafkaReplayTestHarness.ConsumerName;

        public string EventType => TestEventType;

        public int SchemaVersion => TestSchemaVersion;

        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

        public Task HandleAsync(
            IntegrationEventContext context,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
