using MemoryPack;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Ai.Features.CompletePersonalSchedules;

/// <summary>只通知原不可变意图；不在事件中传日程正文、凭据或可替换的操作参数。</summary>
[MemoryPackable]
internal sealed partial record AiScheduleCompletionRequested(Guid OperationId, Guid? TenantId)
{
    internal const string EventType = "fullnet.ai.schedule_completion.requested";
}

/// <summary>Relay外层不加入业务事务；独立scope内调用Calendar本地接收方，再持久化AI结果。</summary>
internal sealed class AiScheduleCompletionRequestedHandler(IIntegrationEventSerializer serializer, IServiceScopeFactory scopes) : IIntegrationEventHandler
{
    public string EventType => AiScheduleCompletionRequested.EventType;
    public int SchemaVersion => 1;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.NaturallyIdempotent;
    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) => throw new InvalidOperationException("需要可信原操作事件信封。");
    public async Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var message = serializer.Deserialize<AiScheduleCompletionRequested>(payload);
        if (context.MessageId == Guid.Empty || context.MessageType != EventType || context.SchemaVersion != 1
            || context.TenantId != message.TenantId || message.OperationId == Guid.Empty) throw new InvalidOperationException("日程完成意图信封不匹配。");
        await using var scope = scopes.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        if (message.TenantId is { } id) tenant.SetTenant(new TenantContext(id, "schedule-completion", "Schedule completion")); else tenant.SetHost();
        await scope.ServiceProvider.GetRequiredService<AiScheduleCompletionService>().DeliverAsync(message.OperationId, true, cancellationToken).ConfigureAwait(false);
    }
}
