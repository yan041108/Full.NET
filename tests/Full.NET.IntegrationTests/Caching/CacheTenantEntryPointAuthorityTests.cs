using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Streaming;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Realtime.SignalR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.IntegrationTests.Caching;

/// <summary>工具、Hub 新连接及聊天续租各自重读真实租户状态，不能沿用另一个实例停用前的准入。</summary>
[TestClass]
[DoNotParallelize]
public sealed class CacheTenantEntryPointAuthorityTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Warm_entry_points_reject_committed_disable_and_authority_failure(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var primary = new FullNetApiFactory(provider, connection);
        using var secondary = primary.CreateIsolatedFactory();
        await primary.InitializeAsync();
        await secondary.InitializeAsync();
        await CacheBackgroundAuthorityFixture.EnsureAnotherActiveTenantAsync(primary);
        using var client = primary.CreateClientForHost("localhost");
        var hostToken = await CacheBackgroundAuthorityFixture.LoginAsync(client, "admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hostToken);
        var tenant = await CacheBackgroundAuthorityFixture.GetAcmeTenantAsync(client, hostToken);
        using var modelResponse = await client.PostAsJsonAsync("/api/v1/ai/model-configs",
            new CreateAiModelConfigRequest(null, "Authority Probe", "ollama", "https://provider.test", "model", null, null, false, true));
        Assert.AreEqual(HttpStatusCode.Created, modelResponse.StatusCode, await modelResponse.Content.ReadAsStringAsync());
        var model = await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>();
        Assert.IsNotNull(model);
        var token = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, hostToken, tenant.Id);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // 当前公开聊天写权限只属于Host；不能为了准备租户续租记录而放宽端点授权。
        using var denied = await client.PostAsJsonAsync("/api/v1/ai/chat/sessions", new CreateAiChatSessionRequest(model.Id, "Authority Probe"));
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode, await denied.Content.ReadAsStringAsync());
        var binding = CacheBackgroundAuthorityFixture.Binding(token, false);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new JsonWebTokenHandler().ReadJsonWebToken(token).Claims, "Testing"));
        await using var scope = secondary.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<ICurrentTenantContextWriter>();
        context.SetTenant(new(tenant.Id, tenant.Identifier, tenant.Name));
        // 内部服务在可信测试作用域写入真实AI记录；这里只验内部租户续租，不声明公开租户聊天可写。
        var prepared = await services.GetRequiredService<AiChatSessionManagementService>()
            .CreateAsync(binding.UserId, new CreateAiChatSessionRequest(model.Id, "Authority Probe"));
        Assert.IsTrue(prepared.IsSuccess, prepared.Error?.Code);
        var session = prepared.Value;
        Assert.IsNotNull(session);
        Assert.AreEqual(tenant.Id, session.TenantId);
        Assert.AreEqual(binding.UserId, session.OwnerUserId);
        var generationId = Guid.CreateVersion7();
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        Assert.AreEqual(1, await db.ExecuteAsync("""
            UPDATE fn_ai_chat_session SET IsGenerating=1, GenerationId=@GenerationId,
                GenerationExpiresAtUtc=@ExpiresAtUtc, GenerationCancellationRequested=0
            WHERE Id=@SessionId AND OwnerUserId=@OwnerUserId AND TenantId=@TenantId
            """, new { GenerationId = generationId, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(2), SessionId = session.Id, OwnerUserId = binding.UserId, TenantId = tenant.Id }));

        var http = services.GetRequiredService<IHttpContextAccessor>();
        var previousHttp = http.HttpContext;
        var tools = services.GetRequiredService<IToolAuthorizationPort>();
        var activity = services.GetRequiredService<ITenantActivityReadPort>();
        var monitor = services.GetRequiredService<AiChatGenerationLeaseMonitor>();
        var chatScope = new AiChatScope(tenant.Id);
        try
        {
            await RequireAllowedAsync();
            for (var cycle = 0; cycle < 2; cycle++)
            {
                await CacheBackgroundAuthorityFixture.SetActiveAsync(primary, tenant.Id, false);
                await RequireRejectedAsync();
                await CacheBackgroundAuthorityFixture.SetActiveAsync(primary, tenant.Id, true);
                await RequireAllowedAsync();
            }

            // 独立库只隐藏租户目录；原会话和聊天租约仍存在，拒绝不能由无效会话造成。
            var hide = provider == DatabaseProvider.SqlServer
                ? "EXEC sp_rename 'dbo.fn_tenancy_tenant', 'fn_tenancy_tenant_authority_fault'"
                : "RENAME TABLE fn_tenancy_tenant TO fn_tenancy_tenant_authority_fault";
            var restore = provider == DatabaseProvider.SqlServer
                ? "EXEC sp_rename 'dbo.fn_tenancy_tenant_authority_fault', 'fn_tenancy_tenant'"
                : "RENAME TABLE fn_tenancy_tenant_authority_fault TO fn_tenancy_tenant";
            await db.ExecuteAsync(hide);
            try
            {
                BindActor();
                await RequireAuthorityFailureAsync(async () => { await tools.AuthorizeAsync(AiAgentRunPermissions.Read, default); });
                using var hub = Hub();
                await RequireAuthorityFailureAsync(hub.OnConnectedAsync);
                Assert.AreEqual(0, ((RecordingGroups)hub.Groups).Joined.Count);
                await RequireCanceledInferenceAsync();
            }
            finally { await db.ExecuteAsync(restore); }
            await RequireAllowedAsync();

            BindActor();
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                { await tools.AuthorizeAsync(AiAgentRunPermissions.Read, canceled.Token); });
        }
        finally { context.Clear(); http.HttpContext = previousHttp; }

        void BindActor()
        {
            context.SetTenant(new(tenant.Id, tenant.Identifier, tenant.Name));
            http.HttpContext = new DefaultHttpContext { User = principal };
        }

        FullNetNotificationHub Hub() => new(activity) { Context = new ProbeHubContext(principal), Groups = new RecordingGroups() };

        async Task RequireAllowedAsync()
        {
            // 每轮主动延长测试夹具租约，避免把上一轮故障耗时造成的到期误认为活动租户被拒绝。
            Assert.AreEqual(1, await db.ExecuteAsync("UPDATE fn_ai_chat_session SET GenerationExpiresAtUtc=@ExpiresAtUtc WHERE Id=@Id AND GenerationId=@GenerationId",
                new { ExpiresAtUtc = DateTime.UtcNow.AddMinutes(2), Id = session.Id, GenerationId = generationId }));
            BindActor();
            var actor = await tools.AuthorizeAsync(AiAgentRunPermissions.Read, default);
            Assert.IsNotNull(actor);
            Assert.AreEqual(tenant.Id, actor.TenantId);
            using var hub = Hub();
            await hub.OnConnectedAsync();
            try
            {
                Assert.IsFalse(((ProbeHubContext)hub.Context).Aborted);
                Assert.AreEqual(2, ((RecordingGroups)hub.Groups).Joined.Count);
            }
            finally { await hub.OnDisconnectedAsync(null); }
            Assert.IsTrue(await monitor.RenewOnceAsync(chatScope, session.Id, binding.UserId, generationId, default));
            Assert.IsFalse(await monitor.RenewOnceAsync(chatScope, session.Id, Guid.CreateVersion7(), generationId, default));
        }

        async Task RequireRejectedAsync()
        {
            // 只比较原始租约值；两库分别返回DateTimeOffset和DateTime，禁止强制转为同一CLR类型。
            var expires = await db.ExecuteScalarAsync<object>("SELECT GenerationExpiresAtUtc FROM fn_ai_chat_session WHERE Id=@Id", new { Id = session.Id });
            Assert.IsNotNull(expires, "租约原始值必须存在，不能以空值相等代替未续租证明。");
            BindActor();
            Assert.IsNull(await tools.AuthorizeAsync(AiAgentRunPermissions.Read, default));
            using var hub = Hub();
            await hub.OnConnectedAsync();
            Assert.IsTrue(((ProbeHubContext)hub.Context).Aborted);
            Assert.AreEqual(0, ((RecordingGroups)hub.Groups).Joined.Count);
            Assert.IsFalse(await monitor.RenewOnceAsync(chatScope, session.Id, binding.UserId, generationId, default));
            await RequireCanceledInferenceAsync();
            Assert.AreEqual(expires, await db.ExecuteScalarAsync<object>("SELECT GenerationExpiresAtUtc FROM fn_ai_chat_session WHERE Id=@Id", new { Id = session.Id }));
        }

        async Task RequireCanceledInferenceAsync()
        {
            using var inference = new CancellationTokenSource();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await monitor.WatchAsync(chatScope, session.Id, binding.UserId, generationId,
                DateTimeOffset.UtcNow.AddMinutes(2), inference, deadline.Token).WaitAsync(deadline.Token);
            Assert.IsTrue(inference.IsCancellationRequested, "租户停用或权威读取故障必须中止推理预算。");
        }
    }

    private static async Task RequireAuthorityFailureAsync(Func<Task> operation)
    {
        Exception? failure = null;
        try { await operation(); }
        catch (Exception exception) { failure = exception; }
        Assert.IsNotNull(failure, "权威查询故障不能返回陈旧的授权成功。");
        Assert.IsFalse(failure is OperationCanceledException, "故障注入没有取消请求，不能将取消误认为数据库读取失败。");
    }

    private sealed class ProbeHubContext(ClaimsPrincipal user) : HubCallerContext
    {
        public bool Aborted { get; private set; }
        public override string ConnectionId { get; } = Guid.CreateVersion7().ToString("N");
        public override string? UserIdentifier => user.FindFirstValue("sub");
        public override ClaimsPrincipal User => user;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() => Aborted = true;
    }

    private sealed class RecordingGroups : IGroupManager
    {
        public HashSet<string> Joined { get; } = [];
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Joined.Add(groupName); return Task.CompletedTask; }
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Joined.Remove(groupName); return Task.CompletedTask; }
    }
}
