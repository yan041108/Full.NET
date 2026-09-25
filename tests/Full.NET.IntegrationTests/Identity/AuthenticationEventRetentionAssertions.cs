using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Retention;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>双库认证事件保留仅删除严格早于截止时间的记录。</summary>
internal static class AuthenticationEventRetentionAssertions
{
    public static readonly IReadOnlyDictionary<string, string?> Settings =
        new Dictionary<string, string?>
        {
            ["Identity:AuthenticationEvents:Retention:Enabled"] = "true",
            ["Identity:AuthenticationEvents:Retention:RetentionDays"] = "365",
            ["Identity:AuthenticationEvents:Retention:BatchSize"] = "2",
            ["Identity:AuthenticationEvents:Retention:MaxBatchesPerRun"] = "1",
        };

    public static async Task VerifyAsync(FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken, useSchemaTemplate: false);
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var tenant = services.GetRequiredService<ICurrentTenantContextWriter>();
        tenant.SetHost();
        try
        {
            var command = services.GetRequiredService<ICommandExecutor>();
            var query = services.GetRequiredService<IQueryExecutor>();
            var oldId = Guid.CreateVersion7();
            var recentId = Guid.CreateVersion7();
            var now = DateTimeOffset.UtcNow;
            await InsertAsync(command, oldId, now.AddDays(-400), cancellationToken);
            await InsertAsync(command, recentId, now.AddDays(-1), cancellationToken);

            var result = await services.GetRequiredService<AuthenticationEventRetentionRunner>()
                .RunOnceAsync(new AuthenticationEventRetentionOptions
                {
                    Enabled = true, RetentionDays = 365, BatchSize = 2, MaxBatchesPerRun = 1
                }, cancellationToken);
            Assert.AreEqual(1, result.Deleted);

            var oldEvent = await query.QuerySingleOrDefaultAsync<AuthenticationEventResponse>(
                AuthenticationEventSql.GetById,
                IdentitySqlParameters.Create(("Id", oldId)), cancellationToken);
            var recentEvent = await query.QuerySingleOrDefaultAsync<AuthenticationEventResponse>(
                AuthenticationEventSql.GetById,
                IdentitySqlParameters.Create(("Id", recentId)), cancellationToken);
            Assert.IsNull(oldEvent);
            Assert.IsNotNull(recentEvent);

            var concurrentIds = Enumerable.Range(0, 4)
                .Select(_ => Guid.CreateVersion7()).ToArray();
            foreach (var id in concurrentIds)
            {
                await InsertAsync(command, id, now.AddDays(-400), cancellationToken);
            }

            // 两个独立 Worker scope 同时领取，必须各守住批量边界且不能重复删除。
            var concurrent = await Task.WhenAll(
                RunConcurrentBatchAsync(factory, cancellationToken),
                RunConcurrentBatchAsync(factory, cancellationToken));
            Assert.AreEqual(4, concurrent.Sum(item => item.Deleted));
            Assert.IsTrue(concurrent.All(item => item.Deleted <= 2));
            foreach (var id in concurrentIds)
            {
                Assert.IsNull(await query.QuerySingleOrDefaultAsync<AuthenticationEventResponse>(
                    AuthenticationEventSql.GetById,
                    IdentitySqlParameters.Create(("Id", id)), cancellationToken));
            }
        }
        finally
        {
            tenant.Clear();
        }
    }

    private static async Task<AuthenticationEventRetentionResult> RunConcurrentBatchAsync(
        FullNetApiFactory factory, CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        tenant.SetHost();
        try
        {
            return await scope.ServiceProvider.GetRequiredService<AuthenticationEventRetentionRunner>()
                .RunOnceAsync(new AuthenticationEventRetentionOptions
                {
                    Enabled = true, RetentionDays = 365, BatchSize = 2, MaxBatchesPerRun = 1
                }, cancellationToken);
        }
        finally
        {
            tenant.Clear();
        }
    }

    private static async Task InsertAsync(ICommandExecutor command, Guid id,
        DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
    {
        var audit = new AuthAuditEvent(id, null, null, new string('0', 64),
            "integration.retention", "integration.retention", true,
            null, null, null, occurredAtUtc);
        Assert.AreEqual(1, await command.ExecuteAsync(
            IdentitySql.InsertAuthAudit, audit, cancellationToken));
    }
}
