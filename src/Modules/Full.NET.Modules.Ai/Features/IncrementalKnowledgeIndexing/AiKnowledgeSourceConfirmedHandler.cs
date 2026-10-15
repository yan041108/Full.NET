using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Abstractions.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;

/// <summary>至少一次投递按不可变版本序号收敛；不创建用户会话或有费用的执行任务。</summary>
internal sealed class AiKnowledgeSourceConfirmedHandler(IIntegrationEventSerializer serializer,
    IServiceScopeFactory scopes) : IIntegrationEventHandler
{
    public string EventType => AiKnowledgeSourceConfirmed.EventType;
    public int SchemaVersion => 1;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Knowledge source delivery requires a trusted event envelope.");

    public async Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var source = serializer.Deserialize<AiKnowledgeSourceConfirmed>(payload);
        if (context.MessageId == Guid.Empty || context.MessageType != EventType || context.SchemaVersion != SchemaVersion || context.TenantId != source.TenantId)
            throw new InvalidOperationException("Knowledge source event envelope does not match its payload.");
        // Relay 外层保持 Host 以更新 Outbox；可信信封只绑定新的消费作用域，不能污染下一条消息。
        await using var delivery = scopes.CreateAsyncScope();
        var current = delivery.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        if (context.TenantId is { } tenantId) current.SetTenant(new TenantContext(tenantId, "ai-source-event", "AI source event"));
        else current.SetHost();
        await delivery.ServiceProvider.GetRequiredService<AiKnowledgeIncrementalIndexWriter>().ApplyAsync(source, cancellationToken).ConfigureAwait(false);
    }
}
