using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;

/// <summary>至少一次通知只修复当前来源意图；信封范围不污染 Relay，也不授予会话或模型调用资格。</summary>
internal sealed class AiKnowledgeDocumentMetadataUpdatedHandler(IIntegrationEventSerializer serializer, IServiceScopeFactory scopes) : IIntegrationEventHandler
{
    public string EventType => AiKnowledgeDocumentMetadataUpdated.EventType;
    public int SchemaVersion => 1;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.NaturallyIdempotent;
    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Knowledge metadata delivery requires a trusted event envelope.");

    public async Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var change = serializer.Deserialize<AiKnowledgeDocumentMetadataUpdated>(payload);
        if (change is null || context.MessageId == Guid.Empty || context.MessageType != EventType || context.SchemaVersion != SchemaVersion || context.TenantId != change.TenantId
            || change.KnowledgeBaseId == Guid.Empty || change.DocumentId == Guid.Empty || change.MetadataVersion < 1 || change.TenantId == Guid.Empty)
            throw new InvalidOperationException("Knowledge metadata event envelope or identity is invalid.");
        // 消费者独立建立范围；外层仍为 Host，以便 Relay 更新自己的 Outbox 租约。
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var writer = provider.GetRequiredService<ICurrentTenantContextWriter>();
        if (change.TenantId is { } id) writer.SetTenant(new(id, "ai-metadata-event", "AI metadata event"));
        else writer.SetHost();
        var tenant = provider.GetRequiredService<ICurrentTenant>();
        var scoped = AiKnowledgeScope.IsTenant(tenant);
        var database = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var source = await provider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<AiKnowledgeSourceConfirmed>(
            AiKnowledgeMetadataSourceSql.For(database.Provider, scoped), new Dictionary<string, object?>
            {
                ["KnowledgeBaseId"] = change.KnowledgeBaseId, ["DocumentId"] = change.DocumentId, ["MetadataVersion"] = change.MetadataVersion
            }, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (tenant.Id != change.TenantId || scoped != change.TenantId.HasValue || AiKnowledgeScope.IsTenant(tenant) != scoped)
            throw new InvalidOperationException("Knowledge metadata scope changed during source lookup.");
        if (source is null) return;
        if (source.KnowledgeBaseId != change.KnowledgeBaseId || source.DocumentId != change.DocumentId || source.TenantId != change.TenantId)
            throw new InvalidOperationException("Knowledge metadata source identity does not match the event.");
        // 不把旧通知解释为旧文件：写入器在文档锁内再次拒绝删除、停用或已过时来源。
        _ = await provider.GetRequiredService<AiKnowledgeIncrementalIndexWriter>().ApplyAsync(source, cancellationToken).ConfigureAwait(false);
    }
}
