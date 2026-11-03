using Full.NET.Abstractions.Messaging;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>协作四条流显式接入Kafka；默认仍由Legacy拥有，只有受控切流后才追加/消费正式流。</summary>
internal static class WorkflowCollaborationKafkaRegistration
{
    internal const string Consumer = "fullnet.workflow.collaboration";
    internal static void Register(IServiceCollection services)
    {
        foreach (var (type, topic) in Streams)
        {
            if (!services.Any(item => item.ImplementationInstance is IntegrationEventTopicDefinition value && value.TopicCode == topic))
                services.AddSingleton(IntegrationEventTopicDefinition.Create(topic, type, 1, EventDeliveryOwner.LegacyPolling));
        }
        // 只解析具体Handler，避免目录构造枚举所有Handler而递归解析Outbox目录。
        services.TryAddScoped<WorkflowCollaborationRequestedHandler>();
        services.TryAddScoped<WorkflowCollaborationCreatedHandler>();
        services.TryAddScoped<WorkflowCollaborationCompletedHandler>();
        services.TryAddScoped<WorkflowCollaborationCancelledHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntegrationEventSubscription, WorkflowCollaborationRequestedKafkaSubscription>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntegrationEventSubscription, WorkflowCollaborationCreatedKafkaSubscription>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntegrationEventSubscription, WorkflowCollaborationCompletedKafkaSubscription>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntegrationEventSubscription, WorkflowCollaborationCancelledKafkaSubscription>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIntegrationEventHandlerRegistry, global::Full.NET.Generated.IntegrationEventHandlerRegistry>());
    }

    internal static readonly (string EventType, string Topic)[] Streams = [
        (WorkflowCollaborationIntegrationEventTypes.Requested, "workflow.collaboration.requested.v1"),
        (WorkflowCollaborationIntegrationEventTypes.Created, "workflow.collaboration.created.v1"),
        (WorkflowCollaborationIntegrationEventTypes.Completed, "workflow.collaboration.completed.v1"),
        (WorkflowCollaborationIntegrationEventTypes.Cancelled, "workflow.collaboration.cancelled.v1")];
}

/// <summary>保留原信封与自然幂等处理，不在适配器中切换租户、写业务或重新序列化。</summary>
internal abstract class WorkflowCollaborationKafkaSubscription(IIntegrationEventHandler handler) : IIntegrationEventSubscription
{
    public string ConsumerName => WorkflowCollaborationKafkaRegistration.Consumer;
    public string EventType => handler.EventType;
    public int SchemaVersion => handler.SchemaVersion;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => handler.IdempotencyStrategy;
    public Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken token) => handler.HandleAsync(context, payload, token);
}

[IntegrationEventSubscription(WorkflowCollaborationKafkaRegistration.Consumer, WorkflowCollaborationIntegrationEventTypes.Requested, 1)]
internal sealed class WorkflowCollaborationRequestedKafkaSubscription(WorkflowCollaborationRequestedHandler handler) : WorkflowCollaborationKafkaSubscription(handler);
[IntegrationEventSubscription(WorkflowCollaborationKafkaRegistration.Consumer, WorkflowCollaborationIntegrationEventTypes.Created, 1)]
internal sealed class WorkflowCollaborationCreatedKafkaSubscription(WorkflowCollaborationCreatedHandler handler) : WorkflowCollaborationKafkaSubscription(handler);
[IntegrationEventSubscription(WorkflowCollaborationKafkaRegistration.Consumer, WorkflowCollaborationIntegrationEventTypes.Completed, 1)]
internal sealed class WorkflowCollaborationCompletedKafkaSubscription(WorkflowCollaborationCompletedHandler handler) : WorkflowCollaborationKafkaSubscription(handler);
[IntegrationEventSubscription(WorkflowCollaborationKafkaRegistration.Consumer, WorkflowCollaborationIntegrationEventTypes.Cancelled, 1)]
internal sealed class WorkflowCollaborationCancelledKafkaSubscription(WorkflowCollaborationCancelledHandler handler) : WorkflowCollaborationKafkaSubscription(handler);
