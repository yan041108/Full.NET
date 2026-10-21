using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Api;

/// <summary>以双库实际执行批量 JSON SQL，校验目标上限、租户匹配和重复事件幂等。</summary>
internal static class PerformanceWebhookBatchAssertions
{
    internal static async Task VerifyAsync(IServiceProvider services, DatabaseProvider provider, Action<string> evidence)
    {
        var queries = services.GetRequiredService<IQueryExecutor>();
        var commands = services.GetRequiredService<ICommandExecutor>();
        var transaction = services.GetRequiredService<ICommandTransaction>();
        var clock = services.GetRequiredService<IClock>();
        var tenantId = Guid.CreateVersion7();
        var otherTenant = Guid.CreateVersion7();
        const string eventType = "fullnet.workflow.instance.completed";
        var seed = new SqlStatement("test.performance.webhooks.seed", """
            INSERT INTO fn_webhooks_subscription
                (Id,TenantId,EventType,TargetUrl,SigningSecretHash,IsActive,CreatedAtUtc,UpdatedAtUtc,Version)
            VALUES (@Id,@TenantId,@EventType,'https://93.184.216.34/hooks','fixture-secret',1,@Now,@Now,1)
            """, SqlDataScope.HostOnly);
        async Task SeedAsync(Guid tenant) => await commands.ExecuteAsync(seed,
            new Dictionary<string,object?> { ["Id"]=Guid.CreateVersion7(),["TenantId"]=tenant,["EventType"]=eventType,["Now"]=clock.UtcNow });
        for (var index=0; index<500; index++) await SeedAsync(tenantId);
        await SeedAsync(otherTenant);
        var enqueue = new WebhookEventEnqueueService(queries, commands, clock,
            services.GetRequiredService<IIdGenerator>(), Options.Create(new DatabaseOptions { Provider=provider }));
        var eventId = Guid.CreateVersion7();
        var payload = new WebhookEventEnvelope(eventType,eventId,tenantId,clock.UtcNow,
            new WebhookWorkflowInstanceCompletedData(Guid.CreateVersion7(),Guid.CreateVersion7(),"performance","fixture"));
        await transaction.ExecuteAsync(async token =>
        {
            await enqueue.EnqueueAsync(tenantId,eventType,eventId,payload,token);
            await enqueue.EnqueueAsync(tenantId,eventType,eventId,payload,token);
            return 0;
        }, default);
        var count = new SqlStatement("test.performance.webhooks.count_event",
            "SELECT COUNT(*) FROM fn_webhooks_delivery WHERE EventId=@EventId", SqlDataScope.HostOnly);
        Assert.AreEqual(500L,await queries.QuerySingleOrDefaultAsync<long>(count,new { EventId=eventId }));
        Assert.AreEqual(1L,await queries.QuerySingleOrDefaultAsync<long>(new SqlStatement("test.performance.webhooks.digest",
            "SELECT COUNT(DISTINCT PayloadDigest) FROM fn_webhooks_delivery WHERE EventId=@EventId",SqlDataScope.HostOnly),new { EventId=eventId }));
        await SeedAsync(tenantId);
        var rejectedEvent = Guid.CreateVersion7();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => enqueue.EnqueueAsync(tenantId,eventType,
            rejectedEvent,payload with { EventId=rejectedEvent }));
        Assert.AreEqual(0L,await queries.QuerySingleOrDefaultAsync<long>(count,new { EventId=rejectedEvent }));
        evidence($"webhook_batch provider={provider} targets=500 duplicate_event_rows=500 other_tenant_excluded=verified overflow_501_writes=0");
    }
}
