using System.Text.Json;
using System.Data.Common;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>双库独立连接验证取消、过期和接替后旧生成不能提交成功。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AiChatGenerationFenceTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Cancellation_and_expired_lease_reject_late_completion(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var stamp = Guid.CreateVersion7().ToString("N");
        var settings = new Dictionary<string, string?>
        {
            ["Files:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "fullnet-a06-chat-files", stamp),
            ["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "fullnet-a06-chat-keys", stamp),
        };
        await using var first = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        await first.OpenAsync();
        var registry = Environment.GetEnvironmentVariable("FULLNET_A06_CHAT_DATABASE_REGISTRY")
            ?? throw new InvalidOperationException("必须准确登记本批数据库。");
        await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { provider = provider.ToString(), database = first.Database,
            filesRoot = settings["Files:Local:RootPath"], keysRoot = settings["DataProtection:KeyRingPath"] }) + "\n");
        using var factory = new FullNetApiFactory(provider, connection, settings);
        await factory.InitializeAsync();
        Guid tenant;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            context.SetHost();
            var key = "a06-" + stamp;
            var created = await scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>()
                .ProvisionAsync(new(key, "聊天代次围栏", key + ".localhost"));
            Assert.IsTrue(created.IsSuccess);
            tenant = created.Value!.Id;
            context.Clear();
        }
        await using var second = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        await second.OpenAsync();
        var now = DateTime.UtcNow;
        var model = Guid.CreateVersion7();
        await first.ExecuteAsync("""
            INSERT INTO fn_ai_model_config (Id,TenantId,Name,ProviderKey,EndpointBaseUrl,ModelId,IsDefault,IsEnabled,CreatedAtUtc,Version)
            VALUES (@Id,NULL,'a06 fence','ollama','https://provider.test','model',0,1,@Now,1)
            """, new { Id = model, Now = now });
        foreach (var scoped in new[] { false, true })
        foreach (var scenario in new[] { "cancelled", "expired", "replaced", "live" })
        {
            var session = Guid.CreateVersion7(); var owner = Guid.CreateVersion7(); var generation = Guid.CreateVersion7();
            await first.ExecuteAsync("""
                INSERT INTO fn_ai_chat_session (Id,TenantId,OwnerUserId,ModelConfigId,ModelName,Title,MessageCount,IsGenerating,
                    GenerationId,GenerationExpiresAtUtc,GenerationCancellationRequested,CreatedAtUtc,Version)
                VALUES (@Session,@Tenant,@Owner,@Model,'model','fence',1,1,@Generation,@Expires,0,@Now,1)
                """, new { Session = session, Tenant = scoped ? tenant : (Guid?)null, Owner = owner, Model = model,
                    Generation = generation, Expires = now.AddMinutes(2), Now = now });
            await first.ExecuteAsync("""
                INSERT INTO fn_ai_chat_message (Id,SessionId,RoleKey,Content,StatusKey,CreatedAtUtc)
                VALUES (@Id,@Session,'assistant','','streaming',@Now)
                """, new { Id = generation, Session = session, Now = now });
            var scopeParameters = new { SessionId = session, OwnerUserId = owner, ScopeTenantId = scoped ? tenant : (Guid?)null,
                GenerationId = generation, Now = now, ExpiresAtUtc = now.AddMinutes(2) };
            Assert.AreEqual(1, await first.ExecuteAsync(AiChatGenerationSql.Renew.Text, scopeParameters));
            if (scenario == "cancelled") Assert.AreEqual(1, await second.ExecuteAsync(AiChatGenerationSql.RequestCancellation.Text, scopeParameters));
            if (scenario == "expired") Assert.AreEqual(1, await second.ExecuteAsync(
                "UPDATE fn_ai_chat_session SET GenerationExpiresAtUtc=@Expired WHERE Id=@Session", new { Expired = now.AddSeconds(-1), Session = session }));
            if (scenario == "replaced") Assert.AreEqual(1, await second.ExecuteAsync(
                "UPDATE fn_ai_chat_session SET GenerationId=@Next WHERE Id=@Session", new { Next = Guid.CreateVersion7(), Session = session }));
            var written = await first.ExecuteAsync(AiChatSql.UpdateMessage.Text, new { MessageId = generation, GenerationId = generation,
                SessionId = session, Content = "late", StatusKey = "completed", PromptTokens = 1, CompletionTokens = 1,
                RequiresLiveLease = 1, Now = now });
            Assert.AreEqual(scenario == "live" ? 1 : 0, written, $"{provider}/{scoped}/{scenario}: 迟到成功写入必须失败关闭。");
            if (scenario != "live")
            {
                var cleanup = await first.ExecuteAsync(AiChatSql.UpdateMessage.Text, new { MessageId = generation, GenerationId = generation,
                    SessionId = session, Content = "", StatusKey = "cancelled", PromptTokens = (int?)null, CompletionTokens = (int?)null,
                    RequiresLiveLease = 0, Now = now });
                Assert.AreEqual(scenario == "replaced" ? 0 : 1, cleanup, "收尾只能处理仍属于原代次的消息。");
            }
        }
        foreach (var scoped in new[] { false, true })
            await VerifyLockWaitAsync(scoped);
        await first.ExecuteAsync("""
            INSERT INTO fn_ai_model_price (Id,ModelConfigId,ProviderKey,ModelId,Currency,InputPerMillion,
                OutputPerMillion,CachedInputPerMillion,ValidFromUtc)
            VALUES (@Id,@Model,'ollama','model','USD',2,4,1,@From)
            """, new { Id = Guid.CreateVersion7(), Model = model, From = DateTime.UtcNow.AddDays(-1) });
        // 真实双库预算账本与受控 SDK 覆盖成功、未知用量和失败收尾，不向模型服务发请求。
        await AiChatPaidReceiptAssertions.VerifyAsync(factory, first, model);

        async Task VerifyLockWaitAsync(bool scoped)
        {
            foreach (var scenario in new[] { "cancel_wait", "expiry_wait" })
            {
                var session = Guid.CreateVersion7(); var owner = Guid.CreateVersion7(); var generation = Guid.CreateVersion7();
                var start = DateTime.UtcNow;
                await first.ExecuteAsync("""
                    INSERT INTO fn_ai_chat_session (Id,TenantId,OwnerUserId,ModelConfigId,ModelName,Title,MessageCount,IsGenerating,
                        GenerationId,GenerationExpiresAtUtc,GenerationCancellationRequested,CreatedAtUtc,Version)
                    VALUES (@Session,@Tenant,@Owner,@Model,'model','wait fence',1,1,@Generation,@Expires,0,@Now,1)
                    """, new { Session = session, Tenant = scoped ? tenant : (Guid?)null, Owner = owner, Model = model,
                        Generation = generation, Expires = start.AddSeconds(scenario == "expiry_wait" ? 3 : 60), Now = start });
                await first.ExecuteAsync("""
                    INSERT INTO fn_ai_chat_message (Id,SessionId,RoleKey,Content,StatusKey,CreatedAtUtc)
                    VALUES (@Id,@Session,'assistant','','streaming',@Now)
                    """, new { Id = generation, Session = session, Now = start });
                var p = new { SessionId = session, OwnerUserId = owner, TenantId = scoped ? tenant : (Guid?)null,
                    ScopeTenantId = scoped ? tenant : (Guid?)null, MessageId = generation, GenerationId = generation,
                    Now = start, ExpiresAtUtc = start.AddMinutes(1) };
                var lockSql = AiKnowledgeAnswerSql.LockFor(provider, scoped).Text;
                var currentSql = AiKnowledgeAnswerSql.CurrentFor(provider, scoped).Text;
                await using DbTransaction blocker = await second.BeginTransactionAsync();
                if (scenario == "cancel_wait")
                    Assert.AreEqual(1, await second.ExecuteAsync(AiChatGenerationSql.RequestCancellation.Text, p, blocker));
                else
                    Assert.AreEqual(1, await second.ExecuteAsync("UPDATE fn_ai_chat_message SET Content='held' WHERE Id=@Id", new { Id = generation }, blocker));
                await using DbTransaction finishing = await first.BeginTransactionAsync();
                if (scenario == "cancel_wait")
                {
                    var waiting = first.QuerySingleOrDefaultAsync<Guid?>(lockSql, p, finishing);
                    await Task.Delay(300);
                    Assert.IsFalse(waiting.IsCompleted, "完成写入必须等待取消所持的同一会话根锁。");
                    await blocker.CommitAsync();
                    Assert.AreEqual(generation, await waiting);
                    Assert.IsNull(await first.QuerySingleOrDefaultAsync<Guid?>(currentSql, p, finishing), "取消提交后，原代不得完成。");
                }
                else
                {
                    Assert.AreEqual(generation, await first.QuerySingleOrDefaultAsync<Guid?>(lockSql, p, finishing));
                    var waiting = first.ExecuteAsync(AiChatSql.UpdateMessage.Text, new { MessageId = generation, GenerationId = generation,
                        SessionId = session, Content = "late", StatusKey = "completed", PromptTokens = 1, CompletionTokens = 1,
                        RequiresLiveLease = 1, Now = start }, finishing);
                    await Task.Delay(TimeSpan.FromSeconds(4));
                    Assert.IsFalse(waiting.IsCompleted, "消息锁必须真实阻塞完成写入直到租约到期。");
                    await blocker.CommitAsync();
                    Assert.AreEqual(1, await waiting, "模拟写入开始时租约仍有效。");
                    Assert.IsNull(await first.QuerySingleOrDefaultAsync<Guid?>(currentSql, p, finishing), "锁等待跨越期限必须由数据库时间拒绝。");
                }
                await finishing.RollbackAsync();
                var state = await second.QuerySingleAsync<string>("SELECT StatusKey FROM fn_ai_chat_message WHERE Id=@Id", new { Id = generation });
                Assert.AreEqual("streaming", state, "失败的完成事务不得留下 completed 消息。");
            }
        }
    }
}
