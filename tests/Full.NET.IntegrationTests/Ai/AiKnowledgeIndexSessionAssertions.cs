using System.Data;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Identity;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库事务及 Identity 后台授权验证冻结会话；不启用业务 Worker。</summary>
internal static class AiKnowledgeIndexSessionAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory, AiOperationBudgetAssertions.TestClock clock)
    {
        await factory.InitializeAsync(); await using var db = AiOperationBudgetAssertions.Connection(factory);
        var identity = await factory.CreateHostIdentityAsync($"index-session-{Guid.NewGuid():N}",
            [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.Upload],
            password: FullNetApiFactory.TestPassword);
        var session = Snapshot(identity.AccessToken);
        var p = await Seed(); var binding = Binding(p);
        // 独立作用域争抢同一任务，只有一份原绑定；重复不重新排队或改换会话。
        var results = await Task.WhenAll(Use((service, _, _) => service.EnqueueAsync(binding, session, default)),
            Use((service, _, _) => service.EnqueueAsync(binding, session, default)));
        Assert.IsNotNull(results[0]); Assert.AreEqual(results[0], results[1]); var taskId = results[0]!.Value;
        Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_session WHERE IndexTaskId=@Id", new { Id = taskId }));
        var held = await Use((_, tasks, _) => tasks.AcquireAsync(binding, taskId, default)); Assert.IsNotNull(held);
        Assert.AreEqual(session, await Use((service, _, _) => service.ReadHeldAsync(binding, taskId, held.LeaseToken!.Value, default)));
        Assert.IsNotNull(await Use((_, _, services) => services.GetRequiredService<IBackgroundSessionAuthorization>().AuthorizeAsync(session, AiKnowledgePermissions.Read)));
        var legacy = Binding(await Seed());
        var legacyTask = await Use((_, tasks, _) => tasks.EnqueueAsync(legacy, default)); Assert.IsNotNull(legacyTask);
        Assert.IsNull(await Use((service, _, _) => service.EnqueueAsync(legacy, session, default)));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_session WHERE IndexTaskId=@Id", new { Id = legacyTask.Id }));

        var rollback = Binding(await Seed());
        await Assert.ThrowsExactlyAsync<IOException>(() => Use((service, _, _) => service.EnqueueAsync(rollback, session, default), failSecond: true));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE DocumentId=@Id", new { Id = rollback.DocumentId }));
        // 同一资源随后可正常创建，证明真实事务已释放且没有提交第一笔孤立任务。
        Assert.IsNotNull(await Use((service, _, _) => service.EnqueueAsync(rollback, session, default)));

        // 先证明原会话有效，再撤销；不能让登录辅助的首次改密掩盖撤权断言。
        Assert.IsNotNull(await Use((_, _, services) => services.GetRequiredService<IBackgroundSessionAuthorization>().AuthorizeAsync(session, AiKnowledgePermissions.Read)));
        await db.ExecuteAsync("UPDATE fn_identity_refresh_session SET RevokedAtUtc=@Now WHERE Id=@Id", new { Now = clock.UtcNow.UtcDateTime, Id = session.SessionId });
        var frozen = await Use((service, _, _) => service.ReadHeldAsync(binding, taskId, held.LeaseToken!.Value, default)); Assert.AreEqual(session, frozen);
        Assert.IsNull(await Use((_, _, services) => services.GetRequiredService<IBackgroundSessionAuthorization>().AuthorizeAsync(frozen!, AiKnowledgePermissions.Read)));
        Assert.IsNull(await Use((service, _, _) => service.EnqueueAsync(binding, session, default)));
        // 新登录可能轮换安全戳，必须在原会话事务用例完成后进行。
        using var client = factory.CreateClientForHost("localhost");
        var newSession = Snapshot(await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, identity.Username));
        Assert.AreNotEqual(session.SessionId, newSession.SessionId);
        Assert.IsNull(await Use((service, _, _) => service.EnqueueAsync(binding, newSession, default)));

        Assert.AreEqual(session.SessionId, await db.QuerySingleAsync<Guid>("SELECT SessionId FROM fn_ai_knowledge_index_session WHERE IndexTaskId=@Id", new { Id = taskId }));

        Assert.IsNotNull(await Use((_, _, services) => services.GetRequiredService<IBackgroundSessionAuthorization>().AuthorizeAsync(newSession, AiKnowledgePermissions.Read)));

        var invalidated = Binding(await Seed());
        var invalidatedId = await Use((service, _, _) => service.EnqueueAsync(invalidated, newSession, default)); Assert.IsNotNull(invalidatedId);
        var invalidatedLease = await Use((_, tasks, _) => tasks.AcquireAsync(invalidated, invalidatedId.Value, default)); Assert.IsNotNull(invalidatedLease);
        await db.ExecuteAsync("UPDATE fn_identity_user SET SecurityStamp=@Stamp WHERE Id=@Id", new { Stamp = "changed", Id = newSession.UserId });
        Assert.IsNull(await Use((_, _, services) => services.GetRequiredService<IBackgroundSessionAuthorization>().AuthorizeAsync(newSession, AiKnowledgePermissions.Read)));
        Assert.IsNull(await Use((service, _, _) => service.EnqueueAsync(invalidated, newSession, default)));
        await db.ExecuteAsync("UPDATE fn_identity_user SET SecurityStamp=@Stamp WHERE Id=@Id", new { Stamp = newSession.SecurityStamp, Id = newSession.UserId });
        Assert.IsNotNull(await Use((_, _, services) => services.GetRequiredService<IBackgroundSessionAuthorization>().AuthorizeAsync(newSession, AiKnowledgePermissions.Read)));
        // 原生 Dapper 的 DateTime 参数可能按 SQL Server datetime 毫秒精度舍入；精确到期夹具须显式保留精度。
        var expiry = new DynamicParameters(new { Id = newSession.SessionId });
        expiry.Add("Now", clock.UtcNow.UtcDateTime, DbType.DateTime2);
        Assert.AreEqual(1, await db.ExecuteAsync("UPDATE fn_identity_refresh_session SET ExpiresAtUtc=@Now WHERE Id=@Id", expiry));
        Assert.IsTrue(await db.QuerySingleAsync<DateTimeOffset>("SELECT ExpiresAtUtc FROM fn_identity_refresh_session WHERE Id=@Id", new { Id = newSession.SessionId }) <= clock.UtcNow);
        Assert.AreEqual(newSession, await Use((service, _, _) => service.ReadHeldAsync(invalidated, invalidatedId.Value, invalidatedLease.LeaseToken!.Value, default)));
        Assert.IsNull(await Use((_, _, services) => services.GetRequiredService<IBackgroundSessionAuthorization>().AuthorizeAsync(newSession, AiKnowledgePermissions.Read)));
        Assert.IsNull(await Use((service, _, _) => service.EnqueueAsync(invalidated, newSession, default)));

        async Task<Dictionary<string, object?>> Seed()
        {
            var row = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET OwnerUserId=@Owner WHERE Id=@Id", new { Owner = session.UserId, Id = row["KnowledgeBaseId"] });
            row["OwnerUserId"] = session.UserId; return row;
        }
        async Task<T> Use<T>(Func<AiKnowledgeIndexSessionService, AiKnowledgeIndexTaskStore, IServiceProvider, Task<T>> action, bool failSecond = false)
        {
            await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
            services.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var query = services.GetRequiredService<IQueryExecutor>(); var command = services.GetRequiredService<ICommandExecutor>();
            var tenant = services.GetRequiredService<ICurrentTenant>(); var ids = services.GetRequiredService<IIdGenerator>();
            var tasks = new AiKnowledgeIndexTaskStore(query, command, tenant, services.GetRequiredService<IClock>(), ids);
            var service = new AiKnowledgeIndexSessionService(query, failSecond ? new FailBindingCommand(command) : command,
                services.GetRequiredService<ICommandTransaction>(), services.GetRequiredService<IDataTransactionState>(), tenant,
                services.GetRequiredService<IClock>(), ids, tasks, services.GetRequiredService<IBackgroundSessionAuthorization>());
            return await action(service, tasks, services);
        }
    }
    private static AiKnowledgeIndexTaskBinding Binding(Dictionary<string, object?> row) => new((Guid)row["KnowledgeBaseId"]!, (Guid)row["DocumentId"]!,
        (Guid)row["VersionId"]!, 1, 1, (Guid)row["OwnerUserId"]!);
    private static SessionBindingSnapshot Snapshot(string token)
    {
        string Claim(string name) => IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(token, name)!;
        return new(Guid.Parse(Claim("sub")), null, Guid.Parse(Claim(FullNetIdentityClaimTypes.SessionId)),
            Claim(FullNetIdentityClaimTypes.SecurityStamp), Claim(FullNetIdentityClaimTypes.ActorScope), Claim(FullNetIdentityClaimTypes.Scope));
    }
    private sealed class FailBindingCommand(ICommandExecutor inner) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement sql, object? parameters = null, CancellationToken cancellationToken = default) =>
            sql.Name.Contains("index_session", StringComparison.Ordinal) ? throw new IOException("binding write failed") : inner.ExecuteAsync(sql, parameters, cancellationToken);
    }
}
